using Basin.Plasma;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ShadowTests
{
    private sealed class ShadowFixture : IDisposable
    {
        public required ShadowManager Manager;
        public required Basin.Plasma.Protocol.OrgKdeKwinShadowManager Proxy;

        public void Dispose() => Manager.Dispose();
    }

    private static ShadowFixture Start(CompositorTestHost host)
    {
        var manager = new ShadowManager(host.Display, host.Compositor);
        Basin.Plasma.Protocol.OrgKdeKwinShadowManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_shadow_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinShadowManager>(e.Name, 2);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return new ShadowFixture { Manager = manager, Proxy = proxy! };
    }

    private static (WlSurface Surface, Surface Server) MapSurface(
        CompositorTestHost host, int width = 60, int height = 50)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        host.PumpToServer();
        return (surface, host.SurfaceScenes[^1].Surface);
    }

    private static ClientShmBuffer[] AttachAll(
        CompositorTestHost host, Basin.Plasma.Protocol.OrgKdeKwinShadow shadow)
    {
        var buffers = new ClientShmBuffer[8];
        for (var i = 0; i < 8; i++)
        {
            buffers[i] = host.Client.CreateBuffer(10 + i, 12 + i, Fill.Solid(10 + i, 12 + i, 0xFF101010));
        }

        shadow.AttachLeft(buffers[0].Proxy);
        shadow.AttachTopLeft(buffers[1].Proxy);
        shadow.AttachTop(buffers[2].Proxy);
        shadow.AttachTopRight(buffers[3].Proxy);
        shadow.AttachRight(buffers[4].Proxy);
        shadow.AttachBottomRight(buffers[5].Proxy);
        shadow.AttachBottom(buffers[6].Proxy);
        shadow.AttachBottomLeft(buffers[7].Proxy);
        return buffers;
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }

    [Fact]
    public void Shadow_commit_applies_only_the_fields_set_since_the_last_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        AttachAll(host, shadow);
        shadow.SetLeftOffset(WlFixed.FromInt(4));
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();

        var tracked = fixture.Manager.ShadowOf(server);
        Assert.NotNull(tracked);
        var before = new IBuffer?[8];
        for (var i = 0; i < 8; i++)
        {
            before[i] = tracked!.Buffer((ShadowPart)i);
            Assert.NotNull(before[i]);
        }

        Assert.Equal(4, tracked!.LeftOffset);

        var replacement = host.Client.CreateBuffer(30, 6, Fill.Solid(30, 6, 0xFF202020));
        shadow.AttachTop(replacement.Proxy);
        shadow.SetRightOffset(WlFixed.FromInt(9));
        shadow.Commit();
        host.PumpToServer();

        for (var i = 0; i < 8; i++)
        {
            var part = (ShadowPart)i;
            if (part == ShadowPart.Top)
            {
                Assert.NotSame(before[i], tracked.Buffer(part));
                Assert.Equal(30, tracked.Buffer(part)!.Width);
            }
            else
            {
                Assert.Same(before[i], tracked.Buffer(part));
            }
        }

        Assert.Equal(4, tracked.LeftOffset);
        Assert.Equal(9, tracked.RightOffset);
    }

    [Fact]
    public void A_shadow_with_no_surface_commit_is_not_attached()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        AttachAll(host, shadow);
        shadow.Commit();
        host.PumpToServer();

        Assert.Null(fixture.Manager.ShadowOf(server));

        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.ShadowOf(server));
    }

    [Fact]
    public void Unset_takes_effect_at_the_next_surface_commit()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        AttachAll(host, shadow);
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.ShadowOf(server));

        fixture.Proxy.Unset(surface);
        host.PumpToServer();
        Assert.NotNull(fixture.Manager.ShadowOf(server));

        surface.Commit();
        host.PumpToServer();
        Assert.Null(fixture.Manager.ShadowOf(server));
    }

    [Fact]
    public void Every_attached_buffer_is_locked_and_a_replaced_one_is_unlocked_once()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        surface.Commit();
        host.PumpToServer();
        var tracked = fixture.Manager.ShadowOf(server)!;

        var first = host.Client.CreateBuffer(10, 12, Fill.Solid(10, 12, 0xFF101010));
        shadow.AttachLeft(first.Proxy);
        shadow.Commit();
        host.PumpToServer();

        var firstServer = tracked.Buffer(ShadowPart.Left)!;
        Assert.Equal(1, firstServer.LockCount);

        var second = host.Client.CreateBuffer(14, 16, Fill.Solid(14, 16, 0xFF202020));
        shadow.AttachLeft(second.Proxy);
        shadow.Commit();
        host.PumpToServer();

        Assert.Equal(0, firstServer.LockCount);
        Assert.Equal(1, tracked.Buffer(ShadowPart.Left)!.LockCount);
    }

    [Fact]
    public void Destroying_the_shadow_object_removes_the_shadow_immediately()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        AttachAll(host, shadow);
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();
        var tracked = fixture.Manager.ShadowOf(server)!;
        var held = tracked.Buffer(ShadowPart.TopLeft)!;
        Assert.Equal(1, held.LockCount);

        shadow.Destroy();
        host.PumpToServer();

        Assert.Null(fixture.Manager.ShadowOf(server));
        Assert.Equal(0, held.LockCount);
        AssertClientAlive(host);
    }

    [Fact]
    public void Destroying_the_manager_leaves_existing_shadows_working()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        AttachAll(host, shadow);
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();

        fixture.Proxy.Destroy();
        host.PumpToServer();

        var tracked = fixture.Manager.ShadowOf(server)!;
        var replacement = host.Client.CreateBuffer(30, 6, Fill.Solid(30, 6, 0xFF202020));
        shadow.AttachTop(replacement.Proxy);
        shadow.Commit();
        host.PumpToServer();

        Assert.Equal(30, tracked.Buffer(ShadowPart.Top)!.Width);
        AssertClientAlive(host);
    }

    public static TheoryData<string> Renderers => new()
    {
        "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller",
    };

    private static string GoldenName(string name, string renderer) =>
        renderer == "pixman" ? name : $"{name}-{renderer}";

    private static Basin.Plasma.Protocol.OrgKdeKwinShadow AttachNinePatch(
        CompositorTestHost host, ShadowFixture fixture, WlSurface surface)
    {
        var shadow = fixture.Proxy.Create(surface);
        var topLeft = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFFCC2222));
        var topRight = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF22CC22));
        var bottomLeft = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF2222CC));
        var bottomRight = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFFCCCC22));
        var top = host.Client.CreateBuffer(8, 16, Fill.Solid(8, 16, 0xFF22CCCC));
        var bottom = host.Client.CreateBuffer(8, 16, Fill.Solid(8, 16, 0xFFCC22CC));
        var left = host.Client.CreateBuffer(16, 8, Fill.Solid(16, 8, 0xFFCC8822));
        var right = host.Client.CreateBuffer(16, 8, Fill.Solid(16, 8, 0xFF888888));

        shadow.AttachTopLeft(topLeft.Proxy);
        shadow.AttachTopRight(topRight.Proxy);
        shadow.AttachBottomLeft(bottomLeft.Proxy);
        shadow.AttachBottomRight(bottomRight.Proxy);
        shadow.AttachTop(top.Proxy);
        shadow.AttachBottom(bottom.Proxy);
        shadow.AttachLeft(left.Proxy);
        shadow.AttachRight(right.Proxy);
        shadow.SetLeftOffset(WlFixed.FromInt(16));
        shadow.SetTopOffset(WlFixed.FromInt(16));
        shadow.SetRightOffset(WlFixed.FromInt(16));
        shadow.SetBottomOffset(WlFixed.FromInt(16));
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();
        return shadow;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_shadow_nine_patch(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);
        var scene = host.SurfaceScenes[^1];
        var effect = new ShadowEffect(scene, fixture.Manager);
        scene.Tree.SetPosition(50, 35);

        AttachNinePatch(host, fixture, surface);
        Assert.NotNull(fixture.Manager.ShadowOf(server));

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("shadow-ninepatch", renderer));
        effect.Dispose();
    }

    [Fact]
    public void The_nine_patch_covers_the_ring_and_leaves_the_centre_clear()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);
        var scene = host.SurfaceScenes[^1];
        var effect = new ShadowEffect(scene, fixture.Manager);
        scene.Tree.SetPosition(50, 35);
        AttachNinePatch(host, fixture, surface);

        var tree = Assert.IsAssignableFrom<SceneTree>(scene.Tree.Children[0]);
        Assert.True(tree.Enabled);
        var centre = new Box(0, 0, server.Current.Width, server.Current.Height);
        var covered = 0;
        foreach (var child in tree.Children)
        {
            var cell = Assert.IsAssignableFrom<SceneBuffer>(child);
            if (cell.Buffer is null)
            {
                continue;
            }

            covered++;
            var box = new Box(cell.X, cell.Y, cell.DestinationWidth, cell.DestinationHeight);
            Assert.True(
                box.X >= centre.X + centre.Width || box.X + box.Width <= centre.X ||
                box.Y >= centre.Y + centre.Height || box.Y + box.Height <= centre.Y,
                $"cell at {box} overlaps the surface {centre}");
        }

        Assert.Equal(8, covered);
        effect.Dispose();
    }

    [Fact]
    public void A_fractional_offset_lands_on_the_nearest_pixel()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);
        var scene = host.SurfaceScenes[^1];
        var effect = new ShadowEffect(scene, fixture.Manager);

        var shadow = fixture.Proxy.Create(surface);
        var corner = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFFCC2222));
        shadow.AttachTopLeft(corner.Proxy);
        shadow.SetLeftOffset(WlFixed.FromDouble(10.75));
        shadow.SetTopOffset(WlFixed.FromDouble(10.5));
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();

        var tracked = fixture.Manager.ShadowOf(server)!;
        Assert.Equal(10.75, tracked.LeftOffset, 2);
        Assert.Equal(10.5, tracked.TopOffset, 2);

        var tree = Assert.IsAssignableFrom<SceneTree>(scene.Tree.Children[0]);
        var topLeft = Assert.IsAssignableFrom<SceneBuffer>(tree.Children[(int)ShadowPart.TopLeft]);
        Assert.Equal(-11, topLeft.X);
        Assert.Equal(-11, topLeft.Y);
        effect.Dispose();
    }

    [Fact]
    public void A_destroyed_surface_releases_all_eight_locks()
    {
        using var host = new CompositorTestHost();
        using var fixture = Start(host);
        var (surface, server) = MapSurface(host);

        var shadow = fixture.Proxy.Create(surface);
        AttachAll(host, shadow);
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();

        var tracked = fixture.Manager.ShadowOf(server)!;
        var held = new IBuffer[8];
        for (var i = 0; i < 8; i++)
        {
            held[i] = tracked.Buffer((ShadowPart)i)!;
            Assert.Equal(1, held[i].LockCount);
        }

        surface.Destroy();
        host.PumpToServer();

        Assert.True(tracked.IsReleased);
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(0, held[i].LockCount);
        }

        shadow.Destroy();
        AssertClientAlive(host);
    }
}
