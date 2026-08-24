using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class ResizeDragTests
{
    [Fact]
    public void A_right_edge_drag_grows_without_moving()
    {
        var drag = new ResizeDrag(ResizeEdges.Right, new Box(10, 20, 100, 80), 110, 60);
        var box = drag.BoxFor(140, 60, 10, 20);
        Assert.Equal(new Box(10, 20, 130, 80), box);
    }

    [Fact]
    public void A_top_left_drag_anchors_the_bottom_right_corner()
    {
        var drag = new ResizeDrag(ResizeEdges.TopLeft, new Box(10, 20, 100, 80), 10, 20);
        var box = drag.BoxFor(30, 40, 10, 20);
        Assert.Equal(new Box(30, 40, 80, 60), box);
        Assert.Equal(110, box.Right);
        Assert.Equal(100, box.Bottom);
    }

    [Fact]
    public void The_minimum_size_holds()
    {
        var drag = new ResizeDrag(ResizeEdges.Right, new Box(10, 20, 100, 80), 110, 60);
        var box = drag.BoxFor(-500, 60, 10, 20);
        Assert.Equal(32, box.Width);
    }
}
