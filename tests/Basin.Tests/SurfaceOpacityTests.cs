using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SurfaceOpacityTests
{
    [Fact]
    public void An_alpha_free_format_is_opaque_without_a_region()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.Client.Display.Flush();
        host.Loop.Dispatch(0);

        Assert.True(host.SurfaceScenes[0].Content.IsOpaque);

        surface.Dispose();
        host.Loop.Dispatch(0);
    }

    [Fact]
    public void An_alpha_format_is_opaque_only_with_a_whole_surface_region()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48), WlShm.Format.Argb8888);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.Client.Display.Flush();
        host.Loop.Dispatch(0);

        Assert.False(host.SurfaceScenes[0].Content.IsOpaque);

        using (var region = host.Client.Compositor.CreateRegion())
        {
            region.Add(0, 0, 64, 48);
            surface.SetOpaqueRegion(region);
        }

        surface.Commit();
        host.Client.Display.Flush();
        host.Loop.Dispatch(0);

        Assert.True(host.SurfaceScenes[0].Content.IsOpaque);

        surface.Dispose();
        host.Loop.Dispatch(0);
    }

    [Fact]
    public void A_partial_opaque_region_is_kept_for_culling()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48), WlShm.Format.Argb8888);
        surface.Attach(buffer.Proxy, 0, 0);
        using (var region = host.Client.Compositor.CreateRegion())
        {
            region.Add(8, 8, 40, 30);
            surface.SetOpaqueRegion(region);
        }

        surface.Commit();
        host.Client.Display.Flush();
        host.Loop.Dispatch(0);

        var content = host.SurfaceScenes[0].Content;
        Assert.False(content.IsOpaque);
        Assert.NotNull(content.OpaqueRegion);
        var extents = content.OpaqueRegion!.Extents;
        Assert.Equal(8, extents.X1);
        Assert.Equal(8, extents.Y1);
        Assert.Equal(48, extents.X2);
        Assert.Equal(38, extents.Y2);

        surface.SetOpaqueRegion(null);
        surface.Commit();
        host.Client.Display.Flush();
        host.Loop.Dispatch(0);
        Assert.Null(content.OpaqueRegion);

        surface.Dispose();
        host.Loop.Dispatch(0);
    }
}
