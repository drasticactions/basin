using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SubsurfaceTests
{
    private const uint Red = 0xFFCC0000;
    private const uint Green = 0xFF00CC00;
    private const uint Blue = 0xFF0000CC;
    private const uint Yellow = 0xFFCCCC00;
    private const uint Black = 0xFF000000;

    [Fact]
    public void Subsurface_renders_above_parent_at_committed_position()
    {
        using var host = new CompositorTestHost();
        var (parent, _) = MapSurface(host, 60, 60, Red);

        var childSurface = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(childSurface, parent);
        subsurface.SetPosition(10, 10);
        subsurface.SetDesync();
        var childBuffer = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, Blue));
        childSurface.Attach(childBuffer.Proxy, 0, 0);
        childSurface.Commit();

        parent.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(5, 5));
        Assert.Equal(Blue, host.Pixel(10, 10));
        Assert.Equal(Blue, host.Pixel(29, 29));
        Assert.Equal(Red, host.Pixel(30, 30));
    }

    [Fact]
    public void Place_below_renders_under_the_parent()
    {
        using var host = new CompositorTestHost();
        var (parent, _) = MapSurface(host, 40, 40, Red);

        var childSurface = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(childSurface, parent);
        subsurface.SetDesync();
        subsurface.SetPosition(20, 20);
        subsurface.PlaceBelow(parent);
        var childBuffer = host.Client.CreateBuffer(40, 40, Fill.Solid(40, 40, Green));
        childSurface.Attach(childBuffer.Proxy, 0, 0);
        childSurface.Commit();
        parent.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(30, 30));
        Assert.Equal(Green, host.Pixel(45, 45));
        Assert.Equal(Black, host.Pixel(70, 70));
    }

    [Fact]
    public void Synchronized_commit_waits_for_the_parent()
    {
        using var host = new CompositorTestHost();
        var (parent, _) = MapSurface(host, 50, 50, Red);

        var childSurface = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(childSurface, parent);
        subsurface.SetPosition(0, 0);
        var blue = host.Client.CreateBuffer(10, 10, Fill.Solid(10, 10, Blue));
        childSurface.Attach(blue.Proxy, 0, 0);
        childSurface.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(5, 5));

        parent.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Blue, host.Pixel(5, 5));

        var yellow = host.Client.CreateBuffer(10, 10, Fill.Solid(10, 10, Yellow));
        childSurface.Attach(yellow.Proxy, 0, 0);
        childSurface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Blue, host.Pixel(5, 5));

        parent.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Yellow, host.Pixel(5, 5));
    }

    [Fact]
    public void Set_desync_applies_the_cached_state_immediately()
    {
        using var host = new CompositorTestHost();
        var (parent, _) = MapSurface(host, 50, 50, Red);

        var childSurface = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(childSurface, parent);
        parent.Commit();
        var blue = host.Client.CreateBuffer(10, 10, Fill.Solid(10, 10, Blue));
        childSurface.Attach(blue.Proxy, 0, 0);
        childSurface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Red, host.Pixel(5, 5));

        subsurface.SetDesync();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Blue, host.Pixel(5, 5));
    }

    [Fact]
    public void Nested_synchronized_tree_applies_with_the_top_commit()
    {
        using var host = new CompositorTestHost();
        var (root, _) = MapSurface(host, 60, 60, Red);

        var middleSurface = host.Client.Compositor.CreateSurface();
        var middleSub = host.Client.Subcompositor.GetSubsurface(middleSurface, root);
        middleSub.SetPosition(10, 10);
        var green = host.Client.CreateBuffer(30, 30, Fill.Solid(30, 30, Green));
        middleSurface.Attach(green.Proxy, 0, 0);

        var leafSurface = host.Client.Compositor.CreateSurface();
        var leafSub = host.Client.Subcompositor.GetSubsurface(leafSurface, middleSurface);
        leafSub.SetDesync();
        leafSub.SetPosition(5, 5);
        var blue = host.Client.CreateBuffer(10, 10, Fill.Solid(10, 10, Blue));
        leafSurface.Attach(blue.Proxy, 0, 0);
        leafSurface.Commit();
        middleSurface.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(12, 12));

        root.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Green, host.Pixel(12, 12));
        Assert.Equal(Blue, host.Pixel(16, 16));
    }

    [Fact]
    public void Restacking_latches_on_parent_commit()
    {
        using var host = new CompositorTestHost();
        var (parent, _) = MapSurface(host, 40, 40, Red);

        var childSurface = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(childSurface, parent);
        subsurface.SetDesync();
        var blue = host.Client.CreateBuffer(40, 40, Fill.Solid(40, 40, Blue));
        childSurface.Attach(blue.Proxy, 0, 0);
        childSurface.Commit();
        parent.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Blue, host.Pixel(5, 5));

        subsurface.PlaceBelow(parent);
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Blue, host.Pixel(5, 5));

        parent.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Red, host.Pixel(5, 5));
    }

    private static (WlSurface Surface, ClientShmBuffer Buffer) MapSurface(CompositorTestHost host, int width, int height, uint color)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, color));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        host.PumpToServer();
        return (surface, buffer);
    }
}
