using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandLifecycleTests
{
    [Fact]
    public void Disposing_returns_the_display_number()
    {
        using var host = new CompositorTestHost();

        int number;
        using (var server = new XWaylandServer(host.Display, host.Loop))
        {
            number = server.DisplayNumber;
            Assert.True(File.Exists($"/tmp/.X{number}-lock"), "the lock names the display as taken");
            Assert.False(server.IsRunning, "the server starts lazily, on the first X client");
        }

        Assert.False(File.Exists($"/tmp/.X{number}-lock"), "the lock outlived the server");
        Assert.False(File.Exists($"/tmp/.X11-unix/X{number}"), "the socket outlived the server");

        using var second = new XWaylandServer(host.Display, host.Loop);
        Assert.Equal(number, second.DisplayNumber);
    }

    [Fact]
    public void Concurrent_servers_take_different_displays()
    {
        using var host = new CompositorTestHost();
        using var first = new XWaylandServer(host.Display, host.Loop);
        using var second = new XWaylandServer(host.Display, host.Loop);

        Assert.NotEqual(first.DisplayNumber, second.DisplayNumber);
        Assert.False(first.IsRunning);
        Assert.False(second.IsRunning);
    }
    [Fact]
    public void Keyboard_grab_is_refused_to_anyone_but_xwayland()
    {
        using var host = new CompositorTestHost();
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var grabs = new XWaylandKeyboardGrabManager(host.Display, host.Compositor, host.Seat);
        grabs.RestrictTo(client => ReferenceEquals(client, server.Client));

        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        Basin.XWayland.Protocol.ZwpXwaylandKeyboardGrabManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_xwayland_keyboard_grab_manager_v1")
            {
                proxy = registry.Bind<Basin.XWayland.Protocol.ZwpXwaylandKeyboardGrabManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var grab = proxy!.GrabKeyboard(window.Surface, client.Seat!);
        host.PumpToClient();
        Assert.Null(grabs.GrabbedSurface);
        Assert.False(host.Seat.Keyboard.HasGrab);

        var sync = client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        grab.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void An_honoured_grab_forces_keys_onto_its_surface()
    {
        using var host = new CompositorTestHost();
        using var grabs = new XWaylandKeyboardGrabManager(host.Display, host.Compositor, host.Seat);

        var client = host.Client;
        var grabbed = MappedToplevel.Map(host, client);
        var other = MappedToplevel.Map(host, client);
        var keyboard = client.Seat!.GetKeyboard();

        Basin.XWayland.Protocol.ZwpXwaylandKeyboardGrabManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_xwayland_keyboard_grab_manager_v1")
            {
                proxy = registry.Bind<Basin.XWayland.Protocol.ZwpXwaylandKeyboardGrabManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var enters = new List<Wayland.WlSurface?>();
        keyboard.Enter += (_, e) => enters.Add(e.Surface);
        host.Seat.Keyboard.NotifyEnter(other.ServerSurface);
        host.PumpUntil(() => enters.Count == 1);
        Assert.Equal(other.Surface, enters[0]);

        var grab = proxy!.GrabKeyboard(grabbed.Surface, client.Seat!);
        host.PumpUntil(() => grabs.GrabbedSurface is not null);
        Assert.Same(grabbed.ServerSurface, grabs.GrabbedSurface);

        host.Seat.Keyboard.NotifyEnter(other.ServerSurface);
        host.PumpUntil(() => enters.Count >= 2);
        Assert.Equal(grabbed.Surface, enters[^1]);

        grab.Destroy();
        host.PumpUntil(() => grabs.GrabbedSurface is null);
        Assert.False(host.Seat.Keyboard.HasGrab);
    }

    [Fact]
    public void An_xwayland_crash_unmaps_then_destroys_each_mapped_window()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        var mapped = new List<XWaylandWindow>();
        var events = new List<string>();
        var exited = false;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += window =>
            {
                mapped.Add(window);
                window.Unmapped += () => events.Add("unmapped");
                window.Destroyed += () => events.Add("destroyed");
            };
            wm.Disposed += _ => events.Add("disposed");
        };
        server.Exited += () =>
        {
            exited = true;
            wm?.Dispose();
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+10+10");
        try
        {
            XWaylandTestSupport.Pump(host, () => mapped.Count > 0);
            KillXwayland(server);
            XWaylandTestSupport.Pump(host, () => exited);

            Assert.Equal(["unmapped", "destroyed", "disposed"], events);
            Assert.Empty(wm!.Windows);
        }
        finally
        {
            if (!eyes.HasExited)
            {
                eyes.Kill();
            }

            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    [Fact]
    public void An_xwayland_crash_leaves_no_ghost_in_the_river_window_list()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var fixture = new RiverFixture(320, 240);
        var host = fixture.Host;
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        var exited = false;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            fixture.Server.Adopt(wm);
        };
        server.Exited += () =>
        {
            exited = true;
            wm?.Dispose();
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+10+10");
        try
        {
            Settle(fixture, () => fixture.Windows.Count > 0);
            KillXwayland(server);
            Settle(fixture, () => exited);
            fixture.RequestManageAndSettle();

            Assert.Empty(fixture.Windows);
        }
        finally
        {
            if (!eyes.HasExited)
            {
                eyes.Kill();
            }

            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    [Fact]
    public void A_clean_exit_after_the_last_client_is_not_a_crash_and_a_kill_is()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        var mapped = 0;
        var exits = 0;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += _ => mapped++;
        };
        server.Exited += () =>
        {
            exits++;
            wm?.Dispose();
            wm = null;
        };

        using (var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+10+10"))
        {
            XWaylandTestSupport.Pump(host, () => mapped == 1);
            eyes.Kill();
            eyes.WaitForExit(2000);
            wm!.Dispose();
            wm = null;
            XWaylandTestSupport.Pump(host, () => exits == 1);
        }

        Assert.Equal(0, server.CrashCount);

        using var again = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+10+10");
        try
        {
            XWaylandTestSupport.Pump(host, () => mapped == 2);
            KillXwayland(server);
            XWaylandTestSupport.Pump(host, () => exits == 2);
            Assert.Equal(1, server.CrashCount);
        }
        finally
        {
            if (!again.HasExited)
            {
                again.Kill();
            }

            again.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    private static void KillXwayland(XWaylandServer server)
    {
        var pid = 0;
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(dir), out var candidate)
                && File.Exists($"{dir}/cmdline")
                && File.ReadAllText($"{dir}/cmdline").Split('\0') is ["Xwayland", var display, ..]
                && display == server.DisplayName)
            {
                pid = candidate;
            }
        }

        Assert.NotEqual(0, pid);
        Assert.Equal(0, Kill(pid, 9));
    }

    private static void Settle(RiverFixture fixture, Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            fixture.Host.Loop.Dispatch(10);
            fixture.RequestManageAndSettle();
        }

        Assert.True(condition(), "condition not reached within 15 s");
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
