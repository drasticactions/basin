using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class ResizeRingTests
{
    private static readonly Box Frame = new(100, 100, 200, 150);

    [Theory]
    [InlineData(95, 150, ResizeEdges.Left)]
    [InlineData(305, 150, ResizeEdges.Right)]
    [InlineData(200, 255, ResizeEdges.Bottom)]
    [InlineData(105, 255, ResizeEdges.BottomLeft)]
    [InlineData(295, 255, ResizeEdges.BottomRight)]
    [InlineData(95, 245, ResizeEdges.BottomLeft)]
    [InlineData(200, 150, ResizeEdges.None)]
    [InlineData(50, 150, ResizeEdges.None)]
    [InlineData(200, 90, ResizeEdges.None)]
    public void EdgesAt_names_the_ring_segment(double x, double y, ResizeEdges expected)
    {
        Assert.Equal(expected, ResizeRing.EdgesAt(Frame, x, y, margin: 8, corner: 24));
    }

    [Fact]
    public void EdgesAt_refuses_an_empty_frame()
    {
        Assert.Equal(ResizeEdges.None, ResizeRing.EdgesAt(default, 0, 0, 8, 24));
    }

    [Theory]
    [InlineData(ResizeEdges.Left, "left_side")]
    [InlineData(ResizeEdges.BottomRight, "bottom_right_corner")]
    [InlineData(ResizeEdges.None, "left_ptr")]
    public void CursorFor_maps_the_edges(ResizeEdges edges, string cursor)
    {
        Assert.Equal(cursor, ResizeRing.CursorFor(edges));
    }
}
