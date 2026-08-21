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

}
