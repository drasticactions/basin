using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasGridSourceTests
{
    [Fact]
    public void A_flat_grid_counts_one_line_per_cell_in_each_direction()
    {
        var source = new CanvasGridSource { CellSize = 64 };
        var bounds = new Box(0, 0, 640, 320);
        Assert.Equal(10, source.VerticalLines(bounds));
        Assert.Equal(5, source.HorizontalLines(bounds));
        Assert.Equal(15 * 6, source.VertexCount(bounds));

        var offset = new Box(100, 30, 640, 320);
        Assert.Equal(10, source.VerticalLines(offset));
        Assert.Equal(5, source.HorizontalLines(offset));
    }

    [Fact]
    public void A_warped_grid_draws_every_canvas_line_that_maps_into_the_output()
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 480, edgeScale: 0.15);
        var source = new CanvasGridSource { CellSize = 60, Left = left };
        var bounds = new Box(0, 0, 600, 300);
        Assert.Equal(((360 + 540) / 60) + 1, source.VerticalLines(bounds));

        var vertices = new MeshVertex[source.VertexCount(bounds)];
        source.WriteVertices(bounds, vertices);
        var previous = -1f;
        for (var i = 0; i < source.VerticalLines(bounds); i++)
        {
            var x = vertices[i * 6].X;
            Assert.True(x >= 0 && x <= 600, $"line {i} at {x}");
            Assert.True(x > previous, $"line {i} at {x} after {previous}");
            previous = x;
        }

        Assert.True(vertices[6].X - vertices[0].X < vertices[8 * 6].X - vertices[7 * 6].X, "lines bunch toward the edge");
        Assert.Equal(120f, vertices[8 * 6].X);
        Assert.Equal(180f, vertices[9 * 6].X);
    }

    [Fact]
    public void A_sloped_zone_bends_the_rows_in_segments_and_keeps_the_columns_straight()
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 480, edgeScale: 0.15, slope: 0.3, center: 150);
        var source = new CanvasGridSource { CellSize = 60, Left = left };
        var bounds = new Box(0, 0, 600, 300);
        Assert.Equal(1 + 15, source.RowSegments(bounds));
        var rows = source.HorizontalLines(bounds);
        var columns = source.VerticalLines(bounds);
        Assert.Equal((columns + (rows * 16)) * 6, source.VertexCount(bounds));

        var vertices = new MeshVertex[source.VertexCount(bounds)];
        source.WriteVertices(bounds, vertices);
        for (var i = 0; i < columns; i++)
        {
            Assert.Equal(vertices[i * 6].X, vertices[(i * 6) + 5].X);
            Assert.Equal(0f, vertices[i * 6].Y);
            Assert.Equal(300f, vertices[(i * 6) + 4].Y);
        }

        var row = (columns * 6) + (16 * 6);
        Assert.Equal(60f, vertices[row].Y);
        Assert.Equal(120f, vertices[row].X);
        Assert.Equal(120f, vertices[row + 6 + 1].X);
        Assert.Equal(60f, vertices[row + 6 + 1].Y);
        var edge = vertices[row + (15 * 6)].Y;
        Assert.Equal(0f, vertices[row + (15 * 6)].X);
        Assert.True(Math.Abs(edge - (150 + ((60 - 150) * 1.3))) < 0.01, $"the row at 60 rises toward the top at the edge: {edge}");
    }

    [Fact]
    public void Alpha_scales_the_vertex_colour()
    {
        var source = new CanvasGridSource
        {
            CellSize = 64,
            Color = new RenderColor(0.2f, 0.4f, 0.8f, 1f),
            Alpha = 0.5f,
        };
        var bounds = new Box(0, 0, 128, 64);
        var vertices = new MeshVertex[source.VertexCount(bounds)];
        source.WriteVertices(bounds, vertices);
        Assert.Equal(0.1f, vertices[0].Color.R, 4);
        Assert.Equal(0.2f, vertices[0].Color.G, 4);
        Assert.Equal(0.4f, vertices[0].Color.B, 4);
        Assert.Equal(0.5f, vertices[0].Color.A, 4);

        source.Alpha = 1f;
        source.WriteVertices(bounds, vertices);
        Assert.Equal(source.Color, vertices[0].Color);
    }

    [Fact]
    public void The_grid_renders_on_a_scene_mesh_and_takes_no_input()
    {
        using var host = new CompositorTestHost();
        var mesh = new SceneMesh(host.Scene.Root);
        mesh.Bounds = new Box(0, 0, 160, 120);
        mesh.Source = new CanvasGridSource { CellSize = 32, Color = new RenderColor(1f, 1f, 1f, 1f) };
        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Red(int x, int y) => rgba[((y * host.Target.Width) + x) * 4];
        Assert.True(Red(32, 10) > Red(20, 10), "a vertical line is brighter than the ground beside it");
        Assert.True(Red(10, 32) > Red(10, 20), "a horizontal line is brighter than the ground beside it");
        Assert.Null(host.Scene.NodeAt(32, 10));
    }
}
