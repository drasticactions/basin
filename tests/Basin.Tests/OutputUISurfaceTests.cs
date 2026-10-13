using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputUISurfaceTests
{
    [Fact]
    public void Nothing_is_created_until_the_output_box_is_valid()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var anchored = new OutputUISurface(scene.Root, host);

        Assert.False(anchored.Place(new Box(0, 0, 0, 0), 1.0));
        Assert.False(anchored.IsRealized);
        Assert.False(anchored.IsPlaced);

        Assert.True(anchored.Place(new Box(40, 10, 800, 600), 1.0));
        Assert.True(anchored.IsRealized);
        Assert.Equal(new Box(40, 10, 800, 600), anchored.Bounds);
        Assert.Equal(40, anchored.Node.Node.X);
        Assert.Equal(10, anchored.Node.Node.Y);

        anchored.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void The_anchor_turns_the_output_box_into_the_surface_box()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var anchored = new OutputUISurface(scene.Root, host)
        {
            Anchor = (box, _) => new Box(box.X, box.Bottom - 32, box.Width, 32),
        };

        Assert.True(anchored.Place(new Box(0, 0, 1280, 720), 1.0));
        Assert.Equal(new Box(0, 688, 1280, 32), anchored.Bounds);
        Assert.Equal(688, anchored.Node.Node.Y);
        Assert.Equal(32, anchored.Node.Node.DestinationHeight);

        anchored.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_zoom_shrinks_the_logical_size_and_keeps_the_box_and_the_input_mapping()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var index = new UISurfaceIndex();
        var zoomed = new OutputUISurface(scene.Root, host, index) { Zoom = 2.0 };

        Assert.True(zoomed.Place(new Box(100, 0, 1280, 720), 1.5));
        Assert.Equal(new UISurfaceSize(640, 360, 3.0), zoomed.Surface!.Size);
        Assert.Equal(1280, zoomed.Node.Node.DestinationWidth);
        Assert.Equal(720, zoomed.Node.Node.DestinationHeight);
        Assert.Equal(new Box(100, 0, 1280, 720), zoomed.Bounds);

        var router = new UISurfaceRouter(scene, index);
        zoomed.Publish();
        Assert.True(router.TryLocal(zoomed.Surface!, 300, 200, out var localX, out var localY));
        Assert.Equal(100, localX, 6);
        Assert.Equal(100, localY, 6);

        zoomed.Zoom = 1.0;
        Assert.True(zoomed.Place(new Box(100, 0, 1280, 720), 1.5));
        Assert.Equal(new UISurfaceSize(1280, 720, 1.5), zoomed.Surface!.Size);
        Assert.Null(zoomed.Node.DevicePixels);
        Assert.Equal(1.0, zoomed.Node.InputScale);

        zoomed.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_mode_or_scale_change_reconfigures_the_same_surface()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var anchored = new OutputUISurface(scene.Root, host);
        var realized = 0;
        anchored.Realized += _ => realized++;

        Assert.True(anchored.Place(new Box(0, 0, 640, 480), 1.0));
        var surface = anchored.Surface;

        Assert.True(anchored.Place(new Box(0, 0, 1024, 768), 2.0));
        Assert.Same(surface, anchored.Surface);
        Assert.Equal(2.0, anchored.Scale);
        Assert.Equal(1, realized);
        Assert.Equal(new UISurfaceSize(1024, 768, 2.0), anchored.Surface!.Size);

        anchored.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void The_surface_follows_the_node_across_the_layout()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var branch = new SceneTree(scene.Root);
        branch.SetPosition(100, 50);
        var anchored = new OutputUISurface(branch, host);
        anchored.Realized += surface => Assert.IsType<FalsifierUISurface>(surface);

        Assert.True(anchored.PlaceAt(new Box(4, 6, 200, 100), 1.0));
        Assert.Equal((104, 56), anchored.Node.Node.ScenePosition);

        anchored.Dispose();
        branch.Destroy();
        scene.Root.Destroy();
    }
}
