using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ServerScaleTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    private static Surface FirstServerSurface(CompositorTestHost host, WlSurface surface)
    {
        surface.Commit();
        host.PumpToServer();
        return host.SurfaceScenes[0].Surface;
    }

    private static void FillQuadrants(nint data, int stride, int width, int height)
    {
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(data + y * stride);
                for (var x = 0; x < width; x++)
                {
                    row[x] = (x < width / 2, y < height / 2) switch
                    {
                        (true, true) => 0xFFFF0000u,
                        (false, true) => 0xFF00FF00u,
                        (true, false) => 0xFF0000FFu,
                        _ => 0xFFFFFFFFu,
                    };
                }
            }
        }
    }

    [Fact]
    public void A_buffer_twice_the_logical_size_makes_a_surface_half_its_size()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var server = FirstServerSurface(host, surface);
        host.Compositor.ClientScales.Set(server.Resource.Client, 2);
        Assert.Equal(2, server.ServerScale);

        var buffer = host.Client.CreateBuffer(200, 100, Fill.Solid(200, 100, 0xFF336699));
        var opaque = host.Client.Compositor.CreateRegion();
        opaque.Add(1, 1, 9, 9);
        surface.SetOpaqueRegion(opaque);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(10, 10, 5, 5);
        surface.Commit();
        host.PumpToServer();

        Assert.Equal((100, 50), (server.Current.Width, server.Current.Height));
        var damage = server.Current.SurfaceDamage.Extents;
        Assert.Equal((5, 5, 8, 8), (damage.X1, damage.Y1, damage.X2, damage.Y2));
        var inner = server.Current.Opaque.Extents;
        Assert.Equal((1, 1, 5, 5), (inner.X1, inner.Y1, inner.X2, inner.Y2));
    }

    [Fact]
    public void A_new_surface_takes_its_clients_scale_and_an_override_wins()
    {
        using var host = new CompositorTestHost();
        var first = host.Client.Compositor.CreateSurface();
        var firstServer = FirstServerSurface(host, first);
        host.Compositor.ClientScales.Set(firstServer.Resource.Client, 2);

        Surface? created = null;
        host.Compositor.SurfaceCreated += surface => created = surface;
        var second = host.Client.Compositor.CreateSurface();
        second.Commit();
        host.PumpToServer();
        Assert.Equal(2, created!.ServerScale);

        created.SetServerScale(1);
        var buffer = host.Client.CreateBuffer(200, 100, Fill.Solid(200, 100, 0xFF336699));
        second.Attach(buffer.Proxy, 0, 0);
        second.Commit();
        host.PumpToServer();
        Assert.Equal((200, 100), (created.Current.Width, created.Current.Height));

        host.Compositor.ClientScales.Set(firstServer.Resource.Client, 3);
        Assert.Equal(3, firstServer.ServerScale);
        Assert.Equal(1, created.ServerScale);

        created.ClearServerScale();
        Assert.Equal(3, created.ServerScale);
        Assert.Equal((67, 33), (created.Current.Width, created.Current.Height));
    }

    [Fact]
    public void Pointer_coordinates_reach_the_client_in_its_own_pixels()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client, 200, 100);
        window.ServerSurface.SetServerScale(2);
        var pointer = host.Client.Seat!.GetPointer();
        (double X, double Y) entered = default;
        (double X, double Y) moved = default;
        pointer.Enter += (_, e) => entered = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        pointer.Motion += (_, e) => moved = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10.5, 3);
        host.PumpUntil(() => entered != default);
        Assert.Equal((21.0, 6.0), entered);

        host.Seat.Pointer.NotifyMotion(1, 20, 7.25);
        host.PumpUntil(() => moved != default);
        Assert.Equal((40.0, 14.5), moved);
    }

    [Fact]
    public void A_subsurface_position_is_divided_by_the_parents_scale()
    {
        using var host = new CompositorTestHost();
        var parent = host.Client.Compositor.CreateSurface();
        var parentServer = FirstServerSurface(host, parent);
        parentServer.SetServerScale(2);
        var child = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(child, parent);
        subsurface.SetPosition(20, 20);
        parent.Commit();
        host.PumpToServer();

        var role = Assert.Single(parentServer.SubsurfacesAbove);
        Assert.Equal((10, 10), (role.X, role.Y));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_scaled_surface_draws_at_its_logical_size(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var surface = host.Client.Compositor.CreateSurface();
        var server = FirstServerSurface(host, surface);
        server.SetServerScale(2);
        var buffer = host.Client.CreateBuffer(80, 60, (data, stride) => FillQuadrants(data, stride, 80, 60));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 80, 60);
        surface.Commit();
        host.PumpToServer();

        host.RenderFrame();
        Assert.Equal(0xFFFF0000u, host.Pixel(5, 5));
        Assert.Equal(0xFF00FF00u, host.Pixel(35, 5));
        Assert.Equal(0xFF0000FFu, host.Pixel(5, 25));
        Assert.Equal(0xFFFFFFFFu, host.Pixel(35, 25));
        Assert.Equal(0xFF000000u, host.Pixel(45, 5));
        Assert.Equal(0xFF000000u, host.Pixel(5, 35));
    }
}
