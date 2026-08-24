using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public class ResizeAnchorTests
{
    [Fact]
    public void For_returns_null_without_a_left_or_top_edge()
    {
        Assert.Null(ResizeAnchor.For(ResizeEdges.Right, 10, 20, 100, 50));
        Assert.Null(ResizeAnchor.For(ResizeEdges.Bottom, 10, 20, 100, 50));
        Assert.Null(ResizeAnchor.For(ResizeEdges.BottomRight, 10, 20, 100, 50));
        Assert.Null(ResizeAnchor.For(ResizeEdges.None, 10, 20, 100, 50));
    }

    [Fact]
    public void For_anchors_the_opposite_corner()
    {
        var anchor = ResizeAnchor.For(ResizeEdges.TopLeft, 10, 20, 100, 50);
        Assert.Equal(new ResizeAnchor(ResizeEdges.TopLeft, 110, 70), anchor);
    }

    [Fact]
    public void PositionFor_moves_only_the_anchored_edges()
    {
        var anchor = new ResizeAnchor(ResizeEdges.TopLeft, 110, 70);
        Assert.Equal((30, 40), anchor.PositionFor(80, 30, 10, 20));

        var leftOnly = new ResizeAnchor(ResizeEdges.Left, 110, 70);
        Assert.Equal((30, 20), leftOnly.PositionFor(80, 30, 10, 20));

        var topOnly = new ResizeAnchor(ResizeEdges.Top, 110, 70);
        Assert.Equal((10, 40), topOnly.PositionFor(80, 30, 10, 20));
    }

    [Fact]
    public void AfterCommit_clears_when_the_resize_ended()
    {
        ResizeAnchor? anchor = new ResizeAnchor(ResizeEdges.Left, 110, 70);
        Assert.Equal(anchor, ResizeAnchor.AfterCommit(anchor, resizing: true));
        Assert.Null(ResizeAnchor.AfterCommit(anchor, resizing: false));
    }
}
