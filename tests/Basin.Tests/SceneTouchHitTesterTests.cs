using Basin.Scene;
using Basin.Seat.Backends;
using Xunit;

namespace Basin.Tests;

public class SceneTouchHitTesterTests
{
    [Fact]
    public void TryHit_reports_the_surface_and_local_coordinates()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host, 40, 40);
        host.SurfaceScenes[0].Tree.SetPosition(10, 10);

        var tester = new SceneTouchHitTester(host.Scene);
        Assert.True(tester.TryHit(15, 20, out var hit));
        Assert.Same(surface, hit.Surface);
        Assert.Equal(5, hit.LocalX);
        Assert.Equal(10, hit.LocalY);
    }

    [Fact]
    public void TryHit_misses_outside_every_surface()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 20, 20);

        var tester = new SceneTouchHitTester(host.Scene);
        Assert.False(tester.TryHit(50, 50, out _));
    }

    [Fact]
    public void TryHit_yields_while_suppressed()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40);

        var suppressed = true;
        var tester = new SceneTouchHitTester(host.Scene) { Suppressed = () => suppressed };
        Assert.False(tester.TryHit(5, 5, out _));

        suppressed = false;
        Assert.True(tester.TryHit(5, 5, out _));
    }

    [Fact]
    public void TryMap_follows_the_node_and_refuses_a_destroyed_one()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40);
        var scene = host.SurfaceScenes[0];
        scene.Tree.SetPosition(10, 10);

        var tester = new SceneTouchHitTester(host.Scene);
        Assert.True(tester.TryHit(15, 20, out var hit));
        Assert.True(tester.TryMap(hit.Token, 30, 30, out var localX, out var localY));
        Assert.Equal(20, localX);
        Assert.Equal(20, localY);

        scene.Destroy();
        Assert.False(tester.TryMap(hit.Token, 30, 30, out _, out _));
        Assert.False(tester.TryMap(null, 30, 30, out _, out _));
    }

    private static Surface MapSurface(CompositorTestHost host, int width, int height)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xffff0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        host.PumpToServer();
        return host.SurfaceScenes[0].Surface;
    }
}
