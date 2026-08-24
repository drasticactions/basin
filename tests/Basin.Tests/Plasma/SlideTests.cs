using Basin.Plasma;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SlideTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private sealed class SlideFixture : IDisposable
    {
        public required SlideManager Manager;
        public required Basin.Plasma.Protocol.OrgKdeKwinSlideManager Proxy;

        public void Dispose() => Manager.Dispose();
    }

    private static SlideFixture Start(CompositorTestHost host)
    {
        var manager = new SlideManager(host.Display, host.Compositor);
        Basin.Plasma.Protocol.OrgKdeKwinSlideManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_slide_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinSlideManager>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return new SlideFixture { Manager = manager, Proxy = proxy! };
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }

    private static (WlSurface Surface, Surface Server, SceneSurface Scene) NewSurface(CompositorTestHost host)
    {
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var scene = host.SurfaceScenes[^1];
        return (surface, scene.Surface, scene);
    }

    private static void MapWithSlide(
        CompositorTestHost host,
        SlideFixture fixture,
        WlSurface surface,
        Basin.Plasma.Protocol.OrgKdeKwinSlide slide,
        uint location,
        int offset = 0)
    {
        slide.SetLocation(location);
        if (offset != 0)
        {
            slide.SetOffset(offset);
        }

        slide.Commit();
        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 60, 50);
        surface.Commit();
        host.PumpToServer();
    }

    [Fact]
    public void Slide_commit_applies_only_the_fields_set_since_the_last_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, _) = NewSurface(host);

        var slide = fixture.Proxy.Create(surface);
        slide.SetLocation((uint)Basin.Plasma.Protocol.OrgKdeKwinSlide.Location.Top);
        slide.Commit();
        surface.Commit();
        host.PumpToServer();

        var tracked = fixture.Manager.SlideOf(server)!;
        Assert.Equal(SlideLocation.Top, tracked.Location);
        Assert.Equal(0, tracked.Offset);

        slide.SetOffset(30);
        slide.Commit();
        host.PumpToServer();

        Assert.Equal(SlideLocation.Top, tracked.Location);
        Assert.Equal(30, tracked.Offset);
    }

    [Fact]
    public void Create_takes_effect_at_the_next_surface_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, _) = NewSurface(host);

        var slide = fixture.Proxy.Create(surface);
        slide.Commit();
        host.PumpToServer();
        Assert.Null(fixture.Manager.SlideOf(server));

        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.SlideOf(server));
    }

    [Fact]
    public void Unset_takes_effect_at_the_next_surface_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, _) = NewSurface(host);

        var slide = fixture.Proxy.Create(surface);
        slide.Commit();
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.SlideOf(server));

        fixture.Proxy.Unset(surface);
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.SlideOf(server));

        surface.Commit();
        host.PumpToServer();
        Assert.Null(fixture.Manager.SlideOf(server));
        slide.Release();
        host.PumpToServer();
    }

    [Theory]
    [InlineData(0u, -110.0, 0.0)]
    [InlineData(1u, 0.0, -85.0)]
    [InlineData(2u, 110.0, 0.0)]
    [InlineData(3u, 0.0, 85.0)]
    public void Each_location_starts_the_surface_beyond_its_own_edge(uint location, double dx, double dy)
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, _, scene) = NewSurface(host);
        scene.Tree.SetPosition(50, 35);
        using var effect = new SlideEffect(scene, fixture.Manager, _ => host.Layout.Bounds);

        var slide = fixture.Proxy.Create(surface);
        MapWithSlide(host, fixture, surface, slide, location);

        Assert.True(effect.IsAnimating);
        Assert.Equal(dx, effect.Applied.X);
        Assert.Equal(dy, effect.Applied.Y);
    }

    [Fact]
    public void An_offset_of_zero_starts_flush_and_a_positive_offset_further_out()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);

        var (flushSurface, _, flushScene) = NewSurface(host);
        flushScene.Tree.SetPosition(50, 35);
        using var flushEffect = new SlideEffect(flushScene, fixture.Manager, _ => host.Layout.Bounds);
        MapWithSlide(host, fixture, flushSurface, fixture.Proxy.Create(flushSurface), location: 0);
        Assert.Equal(-110.0, flushEffect.Applied.X);

        var (outSurface, _, outScene) = NewSurface(host);
        outScene.Tree.SetPosition(50, 35);
        using var outEffect = new SlideEffect(outScene, fixture.Manager, _ => host.Layout.Bounds);
        MapWithSlide(host, fixture, outSurface, fixture.Proxy.Create(outSurface), location: 0, offset: 20);
        Assert.Equal(-130.0, outEffect.Applied.X);
    }

    [Fact]
    public void A_slide_attached_after_the_surface_maps_still_begins()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, scene) = NewSurface(host);
        scene.Tree.SetPosition(50, 35);
        using var effect = new SlideEffect(scene, fixture.Manager, _ => host.Layout.Bounds);

        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.True(server.IsMapped);
        Assert.False(effect.IsAnimating);

        var slide = fixture.Proxy.Create(surface);
        slide.SetLocation((uint)Basin.Plasma.Protocol.OrgKdeKwinSlide.Location.Bottom);
        slide.Commit();
        surface.Commit();
        host.PumpToServer();

        Assert.True(effect.IsAnimating);
        Assert.Equal(85.0, effect.Applied.Y);
    }

    [Fact]
    public void An_unmap_runs_the_reverse_animation_and_holds_the_last_buffer()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, scene) = NewSurface(host);
        scene.Tree.SetPosition(50, 35);
        using var effect = new SlideEffect(scene, fixture.Manager, _ => host.Layout.Bounds);

        var slide = fixture.Proxy.Create(surface);
        MapWithSlide(host, fixture, surface, slide, location: 3);
        effect.Step(Tick(0));
        effect.Step(Tick(300));
        effect.Step(Tick(320));
        Assert.False(effect.IsAnimating);

        var content = server.Current.Buffer!;
        Assert.True(content.LockCount > 0);

        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpToServer();

        Assert.True(effect.IsAnimating);
        Assert.True(effect.HoldsBuffer);
        Assert.True(content.LockCount > 0);

        effect.Step(Tick(400));
        Assert.True(effect.IsAnimating);
        Assert.True(content.LockCount > 0);

        effect.Step(Tick(700));
        effect.Step(Tick(720));
        Assert.False(effect.IsAnimating);
        Assert.Equal(0, content.LockCount);
    }

    [Fact]
    public void A_surface_destroyed_mid_animation_cancels_and_releases_the_buffer()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, scene) = NewSurface(host);
        scene.Tree.SetPosition(50, 35);
        var effect = new SlideEffect(scene, fixture.Manager, _ => host.Layout.Bounds);

        var slide = fixture.Proxy.Create(surface);
        MapWithSlide(host, fixture, surface, slide, location: 3);
        effect.Step(Tick(0));
        effect.Step(Tick(300));
        effect.Step(Tick(320));

        var content = server.Current.Buffer!;
        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.True(effect.IsAnimating);
        Assert.True(content.LockCount > 0);

        surface.Destroy();
        host.PumpToServer();

        Assert.False(effect.IsAnimating);
        Assert.Equal(0, content.LockCount);
        AssertClientAlive(host);
    }

    [Fact]
    public void Release_while_set_on_a_surface_removes_the_slide()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, _) = NewSurface(host);

        var slide = fixture.Proxy.Create(surface);
        slide.Commit();
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.SlideOf(server));

        slide.Release();
        host.PumpToServer();

        Assert.Null(fixture.Manager.SlideOf(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void An_invalid_location_is_ignored()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server, _) = NewSurface(host);

        var slide = fixture.Proxy.Create(surface);
        slide.SetLocation((uint)Basin.Plasma.Protocol.OrgKdeKwinSlide.Location.Right);
        slide.Commit();
        surface.Commit();
        host.PumpToServer();
        var tracked = fixture.Manager.SlideOf(server)!;
        Assert.Equal(SlideLocation.Right, tracked.Location);

        slide.SetLocation(7);
        slide.Commit();
        host.PumpToServer();

        Assert.Equal(SlideLocation.Right, tracked.Location);
        AssertClientAlive(host);
    }
}
