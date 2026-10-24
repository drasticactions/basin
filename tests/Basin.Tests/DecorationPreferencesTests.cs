using Basin.Desktop;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class DecorationPreferencesTests
{
    [Fact]
    public void The_last_negotiated_mode_wins_and_goes_with_the_surface()
    {
        using var host = new CompositorTestHost();
        var preferences = new DecorationPreferences(host.Decorations);
        var changes = new List<bool>();
        preferences.Changed += (_, serverSide) => changes.Add(serverSide);
        var window = MappedToplevel.Map(host, host.Client);
        var surface = window.ServerSurface;
        Assert.Null(preferences.Preference(surface));
        Assert.False(preferences.IsServerSide(window.ServerToplevel));

        var decoration = host.Client.DecorationManager!.GetToplevelDecoration(window.Toplevel);
        decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        host.PumpUntil(() => changes.Contains(true));
        Assert.True(preferences.Preference(surface));
        Assert.True(preferences.IsServerSide(window.ServerToplevel));

        preferences.Record(surface, serverSide: false);
        Assert.False(preferences.IsServerSide(window.ServerToplevel));
        Assert.False(changes[^1]);

        decoration.Destroy();
        window.Toplevel.Destroy();
        window.XdgSurface.Destroy();
        window.Surface.Dispose();
        host.PumpToServer();
        Assert.Null(preferences.Preference(surface));
    }

    [Fact]
    public void A_repeat_of_the_same_value_raises_nothing()
    {
        using var host = new CompositorTestHost();
        var preferences = new DecorationPreferences(host.Decorations);
        var changes = new List<bool>();
        preferences.Changed += (_, serverSide) => changes.Add(serverSide);
        var first = MappedToplevel.Map(host, host.Client);

        preferences.Record(first.ServerSurface, serverSide: true);
        preferences.Record(first.ServerSurface, serverSide: true);
        Assert.Equal([true], changes);

        preferences.Record(first.ServerSurface, serverSide: false);
        Assert.Equal([true, false], changes);

        first.Toplevel.Destroy();
        first.XdgSurface.Destroy();
        first.Surface.Dispose();
        host.PumpToServer();
        var second = MappedToplevel.Map(host, host.Client);
        preferences.Record(second.ServerSurface, serverSide: false);
        Assert.Equal([true, false, false], changes);
    }
}
