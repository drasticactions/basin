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

    private static CanvasGridSource FourSides(int mask)
    {
        CanvasWarp? Warp(int bit, int seam, int direction, int zone, int extension, double center)
        {
            if ((mask & bit) == 0)
            {
                return null;
            }

            var warp = new CanvasWarp(direction);
            warp.Layout(seam, direction, zone, extension, 0.15, 0.3, center);
            return warp;
        }

        return new CanvasGridSource
        {
            CellSize = 60,
            Left = Warp(1, 120, -1, 120, 480, 200),
            Right = Warp(2, 480, 1, 120, 480, 200),
            Top = Warp(4, 48, -1, 48, 200, 300),
            Bottom = Warp(8, 352, 1, 48, 200, 300),
        };
    }

    public static TheoryData<int> SideMasks()
    {
        var data = new TheoryData<int>();
        for (var mask = 0; mask < 16; mask++)
        {
            data.Add(mask);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SideMasks))]
    public void The_vertex_count_matches_the_write_for_every_subset(int mask)
    {
        var source = FourSides(mask);
        var bounds = new Box(0, 0, 600, 400);
        var count = source.VertexCount(bounds);
        var vertices = new MeshVertex[count + 6];
        var sentinel = new MeshVertex(-7, -7, -7, -7, default);
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = sentinel;
        }

        source.WriteVertices(bounds, vertices.AsSpan(0, count));
        for (var i = 0; i < count; i++)
        {
            Assert.NotEqual(sentinel, vertices[i]);
        }

        Assert.Equal(sentinel, vertices[count]);
        Assert.True(count <= (((600 / 60) + (400 / 60) + 4) * (1 + 32)) * 6, $"mask {mask}: {count} vertices");
    }

    [Fact]
    public void Top_and_bottom_leave_the_flat_lines_where_they_were()
    {
        var bounds = new Box(0, 0, 600, 400);
        var sides = FourSides(3);
        var all = FourSides(15);
        var before = new MeshVertex[sides.VertexCount(bounds)];
        sides.WriteVertices(bounds, before);
        var after = new MeshVertex[all.VertexCount(bounds)];
        all.WriteVertices(bounds, after);

        var flatRows = new HashSet<float>();
        for (var y = 60; y < 400; y += 60)
        {
            if (y > 48 && y < 352)
            {
                flatRows.Add(y);
            }
        }

        var rowsBefore = RowsAt(before, flatRows);
        var rowsAfter = RowsAt(after, flatRows);
        Assert.Equal(flatRows.Count, rowsBefore.Count);
        Assert.Equal(rowsBefore, rowsAfter);
        Assert.True(all.HorizontalLines(bounds) > sides.HorizontalLines(bounds), "the vertical zones draw more canvas rows");
        Assert.Equal(sides.VerticalLines(bounds), all.VerticalLines(bounds));
    }

    [Fact]
    public void A_top_zone_bends_the_columns_in_segments()
    {
        var source = FourSides(4);
        var bounds = new Box(0, 0, 600, 400);
        Assert.Equal(1 + 6, source.ColumnSegments(bounds));
        var vertices = new MeshVertex[source.VertexCount(bounds)];
        source.WriteVertices(bounds, vertices);
        var line = 0;
        Assert.Equal(0f, vertices[line * 7 * 6].X);
        var edge = vertices[(line * 7 * 6) + (6 * 6) + 0];
        Assert.True(edge.X < 0, $"the column at 0 fans outward at the top edge: {edge.X}");
        var middle = source.VerticalLines(bounds) / 2;
        var centre = vertices[(middle * 7 * 6) + (6 * 6)];
        Assert.True(Math.Abs(centre.X - 300) <= 60, $"a column near the pivot barely moves: {centre.X}");
    }

    private static HashSet<(float X0, float Y0, float X1, float Y1)> RowsAt(MeshVertex[] vertices, HashSet<float> rows)
    {
        var found = new HashSet<(float, float, float, float)>();
        for (var i = 0; i < vertices.Length; i += 6)
        {
            var a = vertices[i];
            var b = vertices[i + 1];
            if (a.Y == b.Y && rows.Contains(a.Y) && a.X != b.X)
            {
                found.Add((a.X, a.Y, b.X, b.Y));
            }
        }

        return found;
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.5, false)]
    [InlineData(1.0, false)]
    [InlineData(1.0, true)]
    [InlineData(0.5, true)]
    public void The_vertex_count_matches_the_write_at_every_corner_radius(double radius, bool taper)
    {
        var source = FourSides(15);
        source.CornerRadius = radius;
        source.CornerTaper = taper;
        var bounds = new Box(0, 0, 600, 400);
        var count = source.VertexCount(bounds);
        var vertices = new MeshVertex[count + 6];
        var sentinel = new MeshVertex(-7, -7, -7, -7, default);
        Array.Fill(vertices, sentinel);
        source.WriteVertices(bounds, vertices.AsSpan(0, count));
        for (var i = 0; i < count; i++)
        {
            Assert.NotEqual(sentinel, vertices[i]);
            Assert.True(float.IsFinite(vertices[i].X) && float.IsFinite(vertices[i].Y), $"radius {radius}: vertex {i} is not finite");
        }

        Assert.Equal(sentinel, vertices[count]);
    }

    private static CanvasGridSource TerraceGrid(bool ends, double shelfScale = 0.4)
    {
        var left = new CanvasWarp();
        left.LayoutTerrace(seam: 180, direction: -1, zoneWidth: 80, shelfWidth: 100, shelfScale: shelfScale, exponent: 2.0, slope: 0.25, center: 300);
        var right = new CanvasWarp(1);
        right.LayoutTerrace(seam: 1200 - 180, direction: 1, zoneWidth: 80, shelfWidth: 100, shelfScale: shelfScale, exponent: 2.0, slope: 0.0, center: 300);
        var source = new CanvasGridSource { CellSize = 32, Left = left, Right = right };
        if (ends)
        {
            var top = new CanvasWarp();
            top.LayoutTerrace(seam: 100, direction: -1, zoneWidth: 40, shelfWidth: 60, shelfScale: shelfScale, exponent: 2.0, slope: 0.25, center: 600);
            var bottom = new CanvasWarp(1);
            bottom.LayoutTerrace(seam: 600 - 100, direction: 1, zoneWidth: 40, shelfWidth: 60, shelfScale: shelfScale, exponent: 2.0, slope: 0.25, center: 600);
            source.Top = top;
            source.Bottom = bottom;
        }

        return source;
    }

    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    [InlineData(false, 8.0)]
    [InlineData(true, 8.0)]
    public void A_terrace_grid_writes_exactly_the_vertices_it_counts(bool ends, double spacing)
    {
        var source = TerraceGrid(ends, 0.1);
        source.MinLineSpacing = spacing;
        var bounds = new Box(0, 0, 1200, 600);
        var count = source.VertexCount(bounds);
        var vertices = new MeshVertex[count + 1];
        var sentinel = new MeshVertex(-7, -7, -7, -7, default);
        vertices[count] = sentinel;
        source.WriteVertices(bounds, vertices);
        Assert.Equal(sentinel, vertices[count]);
        Assert.NotEqual(default, vertices[count - 1]);
    }

    [Fact]
    public void Terrace_columns_continue_past_the_foot_at_the_shelf_spacing()
    {
        var source = TerraceGrid(ends: false);
        var bounds = new Box(0, 0, 1200, 600);
        var right = source.Right!;
        var shelf = new List<double>();
        for (var x = right.FarEdge; x < right.FarEdge + 1000; x += 32)
        {
            var screen = right.ToScreen(x);
            if (screen < bounds.Right)
            {
                shelf.Add(screen);
            }
        }

        Assert.True(shelf.Count > 5, "the shelf shows columns");
        for (var i = 1; i < shelf.Count; i++)
        {
            Assert.Equal(0.4 * 32, shelf[i] - shelf[i - 1], 6);
        }

        var flat = new CanvasGridSource { CellSize = 32 };
        Assert.True(source.HorizontalLines(bounds) > flat.HorizontalLines(bounds), "the shelves show rows the center cannot");
    }

    [Fact]
    public void The_minimum_spacing_skips_shelf_lines_and_keeps_the_center()
    {
        var source = TerraceGrid(ends: false, shelfScale: 0.1);
        var bounds = new Box(0, 0, 1200, 600);
        var all = source.VerticalLines(bounds);
        source.MinLineSpacing = 8;
        var thinned = source.VerticalLines(bounds);
        Assert.True(thinned < all, $"{thinned} of {all} columns");
        var center = new CanvasGridSource { CellSize = 32 };
        var flatColumns = 0;
        for (var x = 180; x <= 1020; x += 32)
        {
            flatColumns++;
        }

        Assert.True(thinned >= flatColumns);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(8.0)]
    public void A_separable_terrace_grid_writes_straight_lines_it_counts(double spacing)
    {
        var source = TerraceGrid(ends: true, 0.1);
        source.Separable = true;
        source.MinLineSpacing = spacing;
        var bounds = new Box(0, 0, 1200, 600);
        var count = source.VertexCount(bounds);
        Assert.Equal((source.VerticalLines(bounds) + source.HorizontalLines(bounds)) * 6, count);
        var vertices = new MeshVertex[count];
        source.WriteVertices(bounds, vertices);
        for (var i = 0; i < count; i += 6)
        {
            var straight = vertices[i].X == vertices[i + 5].X || vertices[i].Y == vertices[i + 1].Y;
            Assert.True(straight, $"segment {i / 6} bends");
        }
    }
    private static MeshVertex[] Write(CanvasGridSource source, in Box bounds)
    {
        var count = source.VertexCount(bounds);
        var vertices = new MeshVertex[count + 6];
        var sentinel = new MeshVertex(-7, -7, -7, -7, default);
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = sentinel;
        }

        source.WriteVertices(bounds, vertices.AsSpan(0, count));
        for (var i = 0; i < count; i++)
        {
            Assert.NotEqual(sentinel, vertices[i]);
        }

        Assert.Equal(sentinel, vertices[count]);
        return vertices[..count];
    }

    private static List<(float X0, float Y0, float X1, float Y1)> Quads(MeshVertex[] vertices)
    {
        var quads = new List<(float, float, float, float)>();
        for (var i = 0; i < vertices.Length; i += 6)
        {
            var x0 = float.MaxValue;
            var y0 = float.MaxValue;
            var x1 = float.MinValue;
            var y1 = float.MinValue;
            for (var j = i; j < i + 6; j++)
            {
                x0 = Math.Min(x0, vertices[j].X);
                y0 = Math.Min(y0, vertices[j].Y);
                x1 = Math.Max(x1, vertices[j].X);
                y1 = Math.Max(y1, vertices[j].Y);
            }

            quads.Add((x0, y0, x1, y1));
        }

        return quads;
    }

    private static void AssertClear(MeshVertex[] vertices, double left, double top, double right, double bottom, double corner = 0)
    {
        var quads = Quads(vertices);
        for (var i = 0; i < quads.Count; i++)
        {
            var (x0, y0, x1, y1) = quads[i];
            var cx = (x0 + x1) / 2.0;
            var cy = (y0 + y1) / 2.0;
            var inside = cx > left + 1 && cx < right - 1 && cy > top + 1 && cy < bottom - 1;
            var rounded = (cx < left + corner || cx > right - corner) && (cy < top + corner || cy > bottom - corner);
            inside &= !rounded;
            Assert.False(inside, $"segment {i} ({x0},{y0})-({x1},{y1}) lies on the plateau");
        }
    }

    private static int Multiples(int cell, int low, int high)
    {
        var count = 0;
        for (var v = ((low / cell) + 1) * cell; v < high; v += cell)
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Without_desktop_lines_a_left_and_right_terrace_clears_the_plateau()
    {
        var source = TerraceGrid(ends: false);
        var bounds = new Box(0, 0, 1200, 600);
        var all = source.VertexCount(bounds);
        var rows = source.HorizontalLines(bounds);
        source.DesktopLines = false;
        var vertices = Write(source, bounds);
        Assert.Equal(all - ((Multiples(32, 180, 1020) + rows) * 6), vertices.Length);
        AssertClear(vertices, 180, double.MinValue, 1020, double.MaxValue);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Without_desktop_lines_four_sides_keep_their_corners_and_end_rows(double radius)
    {
        var source = TerraceGrid(ends: true);
        source.CornerRadius = radius;
        var bounds = new Box(0, 0, 1200, 600);
        var all = source.VertexCount(bounds);
        source.DesktopLines = false;
        var vertices = Write(source, bounds);
        Assert.Equal(all - ((Multiples(32, 180, 1020) + Multiples(32, 100, 500)) * 6), vertices.Length);
        AssertClear(vertices, 180, 100, 1020, 500, radius > 0 ? 48 : 0);
        var quads = Quads(vertices);
        Assert.Contains(quads, q => q.Y0 < 100 && q.Y1 <= 101 && q.X0 <= 192 && q.X1 >= 192);
        Assert.Contains(quads, q => q.Y0 >= 500 && q.Y1 > 501 && q.X0 <= 192 && q.X1 >= 192);
        Assert.Contains(quads, q => q.Y1 < 100 && q.X1 - q.X0 > 600);
    }

    [Fact]
    public void Without_desktop_lines_a_separable_terrace_stops_its_lines_at_the_seams()
    {
        var source = TerraceGrid(ends: true);
        source.Separable = true;
        var bounds = new Box(0, 0, 1200, 600);
        var lines = source.VerticalLines(bounds) + source.HorizontalLines(bounds);
        source.DesktopLines = false;
        var vertices = Write(source, bounds);
        Assert.Equal((lines + Multiples(32, 180, 1020) + Multiples(32, 100, 500)) * 6, vertices.Length);
        AssertClear(vertices, 180, 100, 1020, 500);
        var quads = Quads(vertices);
        var column = quads.FindAll(q => q.X0 == 192);
        Assert.Equal(2, column.Count);
        Assert.Contains(column, q => q.Y0 == 0 && q.Y1 == 100);
        Assert.Contains(column, q => q.Y0 == 500 && q.Y1 == 600);
        var row = quads.FindAll(q => q.Y0 == 128);
        Assert.Equal(2, row.Count);
        Assert.Contains(row, q => q.X0 == 0 && q.X1 == 180);
        Assert.Contains(row, q => q.X0 == 1020 && q.X1 == 1200);
    }

    [Fact]
    public void Without_desktop_lines_a_zoomed_terrace_clears_the_zoomed_plateau()
    {
        var source = TerraceGrid(ends: true);
        source.ViewScale = 0.75;
        source.ViewCenterX = 600;
        source.ViewCenterY = 300;
        source.DesktopLines = false;
        var vertices = Write(source, new Box(0, 0, 1200, 600));
        double Zoom(double v, double center) => center + ((v - center) * 0.75);
        AssertClear(vertices, Zoom(180, 600), Zoom(100, 300), Zoom(1020, 600), Zoom(500, 300), 48);
        Assert.Contains(Quads(vertices), q => q.X1 < Zoom(180, 600));
    }

    [Fact]
    public void Without_desktop_lines_a_warp_zone_with_no_slope_keeps_its_rows()
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 480, edgeScale: 0.15);
        var source = new CanvasGridSource { CellSize = 60, Left = left, DesktopLines = false };
        var bounds = new Box(0, 0, 600, 300);
        var vertices = Write(source, bounds);
        var quads = Quads(vertices);
        Assert.Contains(quads, q => q.Y0 == 60 && q.X0 == 0 && q.X1 == 120);
        Assert.DoesNotContain(quads, q => q.Y0 == 60 && q.X1 > 121);
        AssertClear(vertices, 120, double.MinValue, 600 + 2, double.MaxValue);
    }

    [Fact]
    public void Without_desktop_lines_a_line_on_a_seam_stays()
    {
        var source = TerraceGrid(ends: true);
        source.CellSize = 20;
        source.DesktopLines = false;
        var quads = Quads(Write(source, new Box(0, 0, 1200, 600)));
        Assert.Contains(quads, q => q.X0 == 180 && q.X1 == 181 && q.Y0 <= 100 && q.Y1 >= 500);
        Assert.Contains(quads, q => q.X0 == 1020 && q.X1 == 1021 && q.Y0 <= 100 && q.Y1 >= 500);
        Assert.Contains(quads, q => q.Y0 == 100 && q.Y1 == 101 && q.X0 <= 180 && q.X1 >= 1020);
        Assert.DoesNotContain(quads, q => q.X0 == 200 && q.X1 == 201 && q.Y0 <= 100 && q.Y1 >= 500);
    }

    [Theory]
    [MemberData(nameof(SideMasks))]
    public void Without_desktop_lines_the_vertex_count_matches_the_write_for_every_subset(int mask)
    {
        foreach (var separable in new[] { false, true })
        {
            foreach (var spacing in new[] { 0.0, 20.0 })
            {
                var source = FourSides(mask);
                source.Separable = separable;
                source.MinLineSpacing = spacing;
                source.DesktopLines = false;
                Write(source, new Box(0, 0, 600, 400));
            }
        }
    }
}
