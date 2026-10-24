using System.Runtime.InteropServices;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class WmCapabilitiesTests
{
    [Fact]
    public void Default_advertises_every_capability()
    {
        Assert.Equal([1u, 2u, 3u, 4u], CapabilitiesSent(null));
    }

    [Fact]
    public void Fullscreen_only_sends_one_wire_value()
    {
        Assert.Equal([3u], CapabilitiesSent(XdgWmCapabilities.Fullscreen));
    }

    [Fact]
    public void None_sends_an_empty_array()
    {
        Assert.Equal([], CapabilitiesSent(XdgWmCapabilities.None));
    }

    [Fact]
    public void A_change_after_the_first_configure_is_sent_again_before_a_configure()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        XdgToplevelWindow? server = null;
        host.Shell.NewToplevel += toplevel => server = toplevel;

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        var events = new List<string>();
        uint serial = 0;
        toplevelProxy.WmCapabilitiesEvent += (_, e) =>
            events.Add("caps:" + string.Join(",", MemoryMarshal.Cast<byte, uint>(e.Capabilities).ToArray()));
        xdgSurface.Configure += (_, e) =>
        {
            serial = e.Serial;
            events.Add("configure");
        };
        surface.Commit();
        host.PumpUntil(() => serial != 0);
        xdgSurface.AckConfigure(serial);
        Assert.Equal(["caps:1,2,3,4", "configure"], events);

        events.Clear();
        server!.WmCapabilities = XdgWmCapabilities.Fullscreen;
        host.PumpUntil(() => events.Count >= 2);
        Assert.Equal(["caps:3", "configure"], events);

        events.Clear();
        server.WmCapabilities = XdgWmCapabilities.Fullscreen;
        host.PumpToClient();
        host.PumpToClient();
        Assert.Empty(events);

        surface.Destroy();
    }

    private static uint[] CapabilitiesSent(XdgWmCapabilities? wanted)
    {
        using var host = new CompositorTestHost();
        var client = host.Client;

        if (wanted is { } capabilities)
        {
            host.Shell.NewToplevel += toplevel => toplevel.WmCapabilities = capabilities;
        }

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        uint[]? received = null;
        uint serial = 0;
        toplevelProxy.WmCapabilitiesEvent += (_, e) =>
            received = MemoryMarshal.Cast<byte, uint>(e.Capabilities).ToArray();
        xdgSurface.Configure += (_, e) => serial = e.Serial;
        surface.Commit();
        host.PumpUntil(() => serial != 0);
        xdgSurface.AckConfigure(serial);

        Assert.NotNull(received);
        var result = received;
        surface.Destroy();
        return result;
    }
}
