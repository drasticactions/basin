using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class TilingTests
{
    private static readonly Box Output = new(0, 0, 1920, 1080);

    private static readonly Box WorkArea = new(0, 24, 1920, 1032);

    [Theory]
    [InlineData(0, 500, TileEdge.Left)]
    [InlineData(15, 500, TileEdge.Left)]
    [InlineData(16, 500, TileEdge.None)]
    [InlineData(1903, 500, TileEdge.None)]
    [InlineData(1904, 500, TileEdge.Right)]
    [InlineData(1919, 500, TileEdge.Right)]
    [InlineData(500, 0, TileEdge.Top)]
    [InlineData(500, 24, TileEdge.Top)]
    [InlineData(500, 25, TileEdge.None)]
    [InlineData(0, 0, TileEdge.Left)]
    [InlineData(1919, 1079, TileEdge.Right)]
    [InlineData(500, 500, TileEdge.None)]
    public void The_edge_zones_are_sixteen_pixels_at_the_sides_and_the_panel_strip_at_the_top(int x, int y, TileEdge expected)
    {
        Assert.Equal(expected, Tiling.EdgeAt(new Point(x, y), Output, WorkArea, tiling: true, topTiling: true));
    }

    [Theory]
    [InlineData(-1, 500)]
    [InlineData(1920, 500)]
    [InlineData(500, -1)]
    [InlineData(500, 1080)]
    public void A_pointer_outside_the_output_never_tiles(int x, int y)
    {
        Assert.Equal(TileEdge.None, Tiling.EdgeAt(new Point(x, y), Output, WorkArea, tiling: true, topTiling: true));
    }

    [Fact]
    public void Tiling_off_leaves_only_the_top_edge()
    {
        Assert.Equal(TileEdge.None, Tiling.EdgeAt(new Point(0, 500), Output, WorkArea, tiling: false, topTiling: true));
        Assert.Equal(TileEdge.None, Tiling.EdgeAt(new Point(1919, 500), Output, WorkArea, tiling: false, topTiling: true));
        Assert.Equal(TileEdge.Top, Tiling.EdgeAt(new Point(500, 0), Output, WorkArea, tiling: false, topTiling: true));
    }

    [Fact]
    public void Top_tiling_off_leaves_only_the_sides()
    {
        Assert.Equal(TileEdge.None, Tiling.EdgeAt(new Point(500, 0), Output, WorkArea, tiling: true, topTiling: false));
        Assert.Equal(TileEdge.Left, Tiling.EdgeAt(new Point(0, 0), Output, WorkArea, tiling: true, topTiling: false));
    }

    [Fact]
    public void Without_a_top_panel_the_top_zone_is_the_first_row()
    {
        Assert.Equal(TileEdge.Top, Tiling.EdgeAt(new Point(500, 0), Output, Output, tiling: true, topTiling: true));
        Assert.Equal(TileEdge.None, Tiling.EdgeAt(new Point(500, 1), Output, Output, tiling: true, topTiling: true));
    }

    [Fact]
    public void Left_and_right_halves_cover_the_work_area_exactly()
    {
        Assert.Equal(new Box(0, 24, 960, 1032), Tiling.RectFor(TileEdge.Left, WorkArea));
        Assert.Equal(new Box(960, 24, 960, 1032), Tiling.RectFor(TileEdge.Right, WorkArea));
    }

    [Fact]
    public void An_odd_width_gives_the_extra_column_to_the_right_half()
    {
        var workArea = new Box(0, 0, 101, 50);

        Assert.Equal(new Box(0, 0, 50, 50), Tiling.RectFor(TileEdge.Left, workArea));
        Assert.Equal(new Box(50, 0, 51, 50), Tiling.RectFor(TileEdge.Right, workArea));
    }

    [Fact]
    public void The_top_edge_takes_the_whole_work_area()
    {
        Assert.Equal(WorkArea, Tiling.RectFor(TileEdge.Top, WorkArea));
        Assert.Equal(WorkArea, Tiling.RectFor(TileEdge.None, WorkArea));
    }
}
