using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class DecorationTests
{
    [Fact]
    public void Default_mode_is_client_side()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        var decoration = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        ZxdgToplevelDecorationV1.Mode? configuredMode = null;
        decoration.Configure += (_, e) => configuredMode = e.Mode;

        host.PumpUntil(() => configuredMode is not null);
        Assert.Equal(ZxdgToplevelDecorationV1.Mode.ClientSide, configuredMode);
        Assert.Equal(DecorationMode.ClientSide, host.Decorations.ModeOf(window.ServerToplevel));
    }

    [Fact]
    public void Client_preference_is_honored()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        var decoration = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        var modes = new List<ZxdgToplevelDecorationV1.Mode>();
        decoration.Configure += (_, e) => modes.Add(e.Mode);

        decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        host.PumpUntil(() => modes.Contains(ZxdgToplevelDecorationV1.Mode.ServerSide));
        Assert.Equal(DecorationMode.ServerSide, host.Decorations.ModeOf(window.ServerToplevel));

        decoration.UnsetMode();
        host.PumpUntil(() => modes[^1] == ZxdgToplevelDecorationV1.Mode.ClientSide);
        Assert.Equal(DecorationMode.ClientSide, host.Decorations.ModeOf(window.ServerToplevel));
    }

    [Fact]
    public void Compositor_policy_overrides_preference()
    {
        using var host = new CompositorTestHost();
        host.Decorations.ChooseMode = (_, _) => DecorationMode.ServerSide;
        var window = MappedToplevel.Map(host, host.Client);

        var decoration = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        ZxdgToplevelDecorationV1.Mode? configuredMode = null;
        decoration.Configure += (_, e) => configuredMode = e.Mode;

        decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ClientSide);
        host.PumpUntil(() => configuredMode is not null);
        Assert.Equal(ZxdgToplevelDecorationV1.Mode.ServerSide, configuredMode);
    }

    [Fact]
    public void Mode_survives_a_decoration_destroyed_without_a_commit()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        var first = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        first.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        host.PumpUntil(() => host.Decorations.ModeOf(window.ServerToplevel) == DecorationMode.ServerSide);

        first.Destroy();
        host.PumpToServer();

        var second = host.Client.DecorationManager.GetToplevelDecoration(window.Toplevel);
        ZxdgToplevelDecorationV1.Mode? configuredMode = null;
        second.Configure += (_, e) => configuredMode = e.Mode;
        host.PumpUntil(() => configuredMode is not null);

        Assert.Equal(ZxdgToplevelDecorationV1.Mode.ServerSide, configuredMode);
    }

    [Fact]
    public void A_commit_between_decorations_drops_the_retained_mode()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        var first = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        first.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        host.PumpUntil(() => host.Decorations.ModeOf(window.ServerToplevel) == DecorationMode.ServerSide);

        first.Destroy();
        window.Surface.Commit();
        host.PumpToServer();

        var second = host.Client.DecorationManager.GetToplevelDecoration(window.Toplevel);
        ZxdgToplevelDecorationV1.Mode? configuredMode = null;
        second.Configure += (_, e) => configuredMode = e.Mode;
        host.PumpUntil(() => configuredMode is not null);

        Assert.Equal(ZxdgToplevelDecorationV1.Mode.ClientSide, configuredMode);
    }

    [Fact]
    public void A_retained_mode_expiring_announces_the_revert_to_client_side()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        var decoration = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        host.PumpUntil(() => host.Decorations.ModeOf(window.ServerToplevel) == DecorationMode.ServerSide);

        var announced = new List<DecorationMode>();
        host.Decorations.ModeChanged += (_, mode) => announced.Add(mode);

        decoration.Destroy();
        window.Surface.Commit();
        host.PumpToServer();

        Assert.Contains(DecorationMode.ClientSide, announced);
        Assert.Equal(DecorationMode.ClientSide, host.Decorations.ModeOf(window.ServerToplevel));
        Assert.False(host.Decorations.TryGetPreference(window.ServerToplevel, out _));
    }

    [Fact]
    public void A_decoration_destroyed_before_map_leaves_the_window_client_side_at_map()
    {
        using var host = new CompositorTestHost();
        XdgToplevelWindow? serverToplevel = null;
        host.Shell.NewToplevel += toplevel => serverToplevel ??= toplevel;

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        uint serial = 0;
        xdgSurface.Configure += (_, e) => serial = e.Serial;
        surface.Commit();
        host.PumpUntil(() => serial != 0);
        xdgSurface.AckConfigure(serial);

        var decoration = client.DecorationManager!.GetToplevelDecoration(toplevelProxy);
        decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        host.PumpUntil(() => serverToplevel is not null &&
            host.Decorations.ModeOf(serverToplevel) == DecorationMode.ServerSide);

        var modes = new List<DecorationMode>();
        host.Decorations.ModeChanged += (_, mode) => modes.Add(mode);
        decoration.Destroy();

        var mapped = false;
        serverToplevel!.Xdg.Mapped += () => mapped = true;
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 60, 50);
        surface.Commit();
        host.PumpUntil(() => mapped);

        Assert.Contains(DecorationMode.ClientSide, modes);
        Assert.Equal(DecorationMode.ClientSide, host.Decorations.ModeOf(serverToplevel));
    }

    [Fact]
    public void Second_decoration_object_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        _ = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        host.PumpToServer();
        _ = host.Client.DecorationManager.GetToplevelDecoration(window.Toplevel);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }
}
