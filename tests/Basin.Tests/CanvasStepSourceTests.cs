using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasStepSourceTests
{
    internal static CanvasStepMap Map(CanvasStepSides sides)
    {
        var map = new CanvasStepMap();
        Span<TinyComp.OverviewSide> full = stackalloc TinyComp.OverviewSide[4];
        full[0] = (sides & CanvasStepSides.Left) != 0 ? TinyComp.OverviewLayout.StepFull(0, 0, 800, -1, 0.75, 0.04, 1600) : default;
        full[1] = (sides & CanvasStepSides.Right) != 0 ? TinyComp.OverviewLayout.StepFull(1600, 1600, 800, 1, 0.75, 0.04, 1600) : default;
        full[2] = (sides & CanvasStepSides.Top) != 0 ? TinyComp.OverviewLayout.StepFull(0, 0, 450, -1, 0.75, 0.04, 900) : default;
        full[3] = (sides & CanvasStepSides.Bottom) != 0 ? TinyComp.OverviewLayout.StepFull(900, 900, 450, 1, 0.75, 0.04, 900) : default;
        var box = new Box(0, 0, 1600, 900);
        _ = TinyComp.OverviewLayout.LayoutStep(map, box, box, full, 1.0, 0.75, 0.4);
        return map;
    }

    private static MeshVertex[] Write(CanvasStepSource source, in Box bounds)
    {
        var vertices = new MeshVertex[source.VertexCount(bounds)];
        source.WriteVertices(bounds, vertices);
        return vertices;
    }

    [Fact]
    public void Without_lines_each_active_wall_is_a_base_line()
    {
        var bounds = new Box(0, 0, 1600, 900);
        var two = new CanvasStepSource { Map = Map(CanvasStepSides.Horizontal), Lines = false };
        Assert.Equal(2 * 6, two.VertexCount(bounds));
        var four = new CanvasStepSource { Map = Map(CanvasStepSides.All), Lines = false };
        Assert.Equal(4 * 6, four.VertexCount(bounds));
        Assert.Equal(0, new CanvasStepSource { Map = new CanvasStepMap() }.VertexCount(bounds));
        var faded = new CanvasStepSource { Map = Map(CanvasStepSides.All), Alpha = 0f };
        Assert.Equal(4 * 6, faded.VertexCount(bounds));
    }

    [Fact]
    public void The_shelf_desktop_and_wall_grids_turn_off_one_at_a_time_and_the_base_lines_stay()
    {
        var bounds = new Box(0, 0, 1600, 900);
        var map = Map(CanvasStepSides.All);
        int Count(bool shelf, bool wall, bool desktop) => new CanvasStepSource
        {
            Map = map, CellSize = 64, ShelfLines = shelf, WallGridLines = wall, DesktopLines = desktop,
        }.VertexCount(bounds);

        var all = Count(true, true, true);
        var shelfOnly = all - Count(false, true, true);
        var wallOnly = all - Count(true, false, true);
        var desktopOnly = all - Count(true, true, false);
        Assert.True(shelfOnly > 0 && wallOnly > 0 && desktopOnly > 0);
        var baseLines = new CanvasStepSource { Map = map, CellSize = 64, Lines = false }.VertexCount(bounds);
        Assert.Equal(4 * 6, baseLines);
        Assert.Equal(all - shelfOnly - wallOnly - desktopOnly, baseLines);
        Assert.Equal(baseLines, Count(false, false, false));
    }

    [Fact]
    public void The_vertex_count_matches_what_is_written()
    {
        var bounds = new Box(0, 0, 1600, 900);
        var source = new CanvasStepSource { Map = Map(CanvasStepSides.All), CellSize = 64, MinLineSpacing = 8 };
        var count = source.VertexCount(bounds);
        Assert.True(count > 4 * 6);
        var vertices = Write(source, bounds);
        Assert.DoesNotContain(vertices, vertex => vertex.X == 0 && vertex.Y == 0 && vertex.Color.A == 0);
    }

    [Fact]
    public void Walls_meeting_at_a_corner_share_the_miter()
    {
        var map = Map(CanvasStepSides.All);
        var walls = new CanvasStepSurfaceSource(CanvasStepSurface.Walls) { Map = map };
        var vertices = new MeshVertex[walls.VertexCount(new Box(0, 0, 1600, 900))];
        walls.WriteVertices(new Box(0, 0, 1600, 900), vertices);
        var top = vertices.AsSpan(0, 6).ToArray();
        var right = vertices.AsSpan(12, 6).ToArray();
        var outerCorner = ((float)map.Inner.Right, (float)map.Inner.Y);
        var innerCorner = ((float)map.Outline.Right, (float)map.Outline.Y);
        Assert.Contains(top, vertex => (vertex.X, vertex.Y) == outerCorner);
        Assert.Contains(right, vertex => (vertex.X, vertex.Y) == outerCorner);
        Assert.Contains(top, vertex => (vertex.X, vertex.Y) == innerCorner);
        Assert.Contains(right, vertex => (vertex.X, vertex.Y) == innerCorner);
    }

    [Fact]
    public void The_lit_walls_are_lighter_than_the_shaded_ones_and_darker_at_the_base()
    {
        var source = new CanvasStepSurfaceSource(CanvasStepSurface.Walls) { Map = Map(CanvasStepSides.All) };
        static float Light(RenderColor color) => color.R + color.G + color.B;
        var top = Light(source.WallColorOf(CanvasStepSides.Top));
        var left = Light(source.WallColorOf(CanvasStepSides.Left));
        var right = Light(source.WallColorOf(CanvasStepSides.Right));
        var bottom = Light(source.WallColorOf(CanvasStepSides.Bottom));
        var plain = Light(source.WallColor);
        Assert.True(top > left && left > plain && plain > right && right > bottom);
        Assert.True(Light(source.BaseColorOf(CanvasStepSides.Top)) < top);
        source.WallShade = 0;
        Assert.Equal(source.WallColor, source.WallColorOf(CanvasStepSides.Bottom));
    }

    [Fact]
    public void No_desktop_or_shelf_grid_line_lies_inside_a_wall()
    {
        var map = Map(CanvasStepSides.Horizontal);
        var color = new RenderColor(0.1f, 0.2f, 0.9f, 1f);
        var source = new CanvasStepSource { Map = map, CellSize = 64, MinLineSpacing = 0, Color = color };
        var vertices = Write(source, new Box(0, 0, 1600, 900));
        var d = map.Outline;
        var b = map.Inner;
        var inside = 0;
        for (var i = 0; i < vertices.Length; i += 6)
        {
            if (vertices[i].Color != color)
            {
                continue;
            }

            var cx = 0.0;
            var cy = 0.0;
            for (var j = 0; j < 6; j++)
            {
                cx += vertices[i + j].X / 6.0;
                cy += vertices[i + j].Y / 6.0;
            }

            var inLeft = cx > b.X + 1 && cx < d.X - 1 && cy > d.Y && cy < d.Bottom;
            var inRight = cx > d.Right + 1 && cx < b.Right - 1 && cy > d.Y && cy < d.Bottom;
            if (inLeft || inRight)
            {
                inside++;
            }
        }

        var crossing = 0;
        for (long canvas = 0; canvas <= 900; canvas += 64)
        {
            var screen = Math.Round(map.CenterY + ((canvas - map.CenterY) * map.Zoom));
            crossing += screen >= d.Y && screen <= d.Bottom ? 1 : 0;
        }

        Assert.Equal(2 * (3 + crossing), inside);
    }
}
