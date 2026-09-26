using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasStepSurfaceSourceTests
{
    private static readonly Box Bounds = new(0, 0, 1600, 900);

    private static CanvasStepMap Map(CanvasStepSides sides, double progress = 1.0)
    {
        var map = new CanvasStepMap();
        var full = new TinyComp.OverviewSide[4];
        full[0] = (sides & CanvasStepSides.Left) != 0 ? TinyComp.OverviewLayout.StepFull(0, 0, 800, -1, 0.75, 0.04, 1600) : default;
        full[1] = (sides & CanvasStepSides.Right) != 0 ? TinyComp.OverviewLayout.StepFull(1600, 1600, 800, 1, 0.75, 0.04, 1600) : default;
        full[2] = (sides & CanvasStepSides.Top) != 0 ? TinyComp.OverviewLayout.StepFull(0, 0, 450, -1, 0.75, 0.04, 900) : default;
        full[3] = (sides & CanvasStepSides.Bottom) != 0 ? TinyComp.OverviewLayout.StepFull(900, 900, 450, 1, 0.75, 0.04, 900) : default;
        _ = TinyComp.OverviewLayout.LayoutStep(map, Bounds, Bounds, full, progress, 0.75, 0.4);
        return map;
    }

    private static CanvasStepSurfaceSource Textured(CanvasStepSurface surface, CanvasStepMap map, double scale = 1.0) =>
        new(surface) { Map = map, TextureWidth = 256, TextureHeight = 256, TextureScale = scale };

    private static MeshVertex[] Write(CanvasStepSurfaceSource source)
    {
        var count = source.VertexCount(Bounds);
        var vertices = new MeshVertex[count];
        vertices.AsSpan().Fill(new MeshVertex(float.NaN, float.NaN, float.NaN, float.NaN, default));
        source.WriteVertices(Bounds, vertices);
        Assert.DoesNotContain(vertices, vertex => float.IsNaN(vertex.X) || float.IsNaN(vertex.U));
        Assert.Equal(0, count % 3);
        return vertices;
    }

    [Fact]
    public void Without_a_texture_the_walls_are_the_old_six_vertices_and_the_floor_is_empty()
    {
        var map = Map(CanvasStepSides.All);
        var walls = new CanvasStepSurfaceSource(CanvasStepSurface.Walls) { Map = map };
        var vertices = Write(walls);
        Assert.Equal(4 * 6, vertices.Length);
        CanvasStepSides[] order = [CanvasStepSides.Top, CanvasStepSides.Left, CanvasStepSides.Right, CanvasStepSides.Bottom];
        for (var i = 0; i < 4; i++)
        {
            CanvasStepSurfaceSource.Corners(map, order[i], out var inner0, out var inner1, out var outer0, out var outer1);
            var lit = walls.WallColorOf(order[i]);
            var foot = walls.BaseColorOf(order[i]);
            MeshVertex V((double X, double Y) p, RenderColor c) => new((float)p.X, (float)p.Y, 0, 0, c);
            MeshVertex[] expected = [V(inner0, lit), V(inner1, lit), V(outer1, foot), V(inner0, lit), V(outer1, foot), V(outer0, foot)];
            Assert.Equal(expected, vertices.AsSpan(i * 6, 6).ToArray());
        }

        Assert.Equal(0, new CanvasStepSurfaceSource(CanvasStepSurface.Floor) { Map = map }.VertexCount(Bounds));
        Assert.Equal(0, Textured(CanvasStepSurface.Walls, Map(CanvasStepSides.All, 0.0)).VertexCount(Bounds));
        Assert.Equal(0, Textured(CanvasStepSurface.Floor, Map(CanvasStepSides.All, 0.0)).VertexCount(Bounds));
    }

    [Theory]
    [InlineData(0.75, 0.4)]
    [InlineData(0.6, 0.3)]
    [InlineData(0.9, 0.1)]
    [InlineData(0.5, 0.5)]
    public void The_foreshortening_runs_from_zero_to_one_and_increases(double zoom, double shelf)
    {
        Assert.Equal(0.0, CanvasStepSurfaceSource.Foreshorten(0, zoom, shelf), 12);
        Assert.Equal(1.0, CanvasStepSurfaceSource.Foreshorten(1, zoom, shelf), 12);
        var previous = 0.0;
        for (var i = 1; i <= 100; i++)
        {
            var t = i / 100.0;
            var a = CanvasStepSurfaceSource.Foreshorten(t, zoom, shelf);
            Assert.True(a > previous);
            Assert.True(zoom == shelf || a >= t);
            Assert.Equal(t, CanvasStepSurfaceSource.Unforeshorten(a, zoom, shelf), 9);
            previous = a;
        }

        Assert.Equal(0.65, CanvasStepSurfaceSource.Foreshorten(0.5, 0.75, 0.4), 2);
    }

    [Theory]
    [InlineData(CanvasStepSurface.Walls, 1.0, 1.0)]
    [InlineData(CanvasStepSurface.Walls, 0.5, 1.0)]
    [InlineData(CanvasStepSurface.Walls, 1.0, 0.25)]
    [InlineData(CanvasStepSurface.Floor, 1.0, 1.0)]
    [InlineData(CanvasStepSurface.Floor, 0.5, 1.0)]
    [InlineData(CanvasStepSurface.Floor, 1.0, 0.25)]
    [InlineData(CanvasStepSurface.Floor, 1.0, 8.0)]
    public void The_vertex_count_matches_what_is_written(CanvasStepSurface surface, double progress, double scale)
    {
        foreach (var sides in new[] { CanvasStepSides.All, CanvasStepSides.Horizontal, CanvasStepSides.Top | CanvasStepSides.Right })
        {
            var vertices = Write(Textured(surface, Map(sides, progress), scale));
            Assert.NotEmpty(vertices);
        }
    }

    [Fact]
    public void Every_sample_point_lies_inside_the_texture_and_no_cell_crosses_a_tile()
    {
        foreach (var surface in new[] { CanvasStepSurface.Walls, CanvasStepSurface.Floor })
        {
            var map = Map(CanvasStepSides.All);
            var vertices = Write(Textured(surface, map, 0.5));
            for (var i = 0; i < vertices.Length; i += 3)
            {
                for (var j = 0; j < 3; j++)
                {
                    Assert.InRange(vertices[i + j].U, 0f, 256f);
                    Assert.InRange(vertices[i + j].V, 0f, 256f);
                }

                if (surface == CanvasStepSurface.Floor)
                {
                    var column = double.NaN;
                    var row = double.NaN;
                    for (var j = 0; j < 3; j++)
                    {
                        var (cx, cy) = map.ToCanvas(CanvasStepPlane.Shelf, vertices[i + j].X, vertices[i + j].Y);
                        var c = Math.Round(((cx / 0.5) - vertices[i + j].U) / 256.0);
                        var r = Math.Round(((cy / 0.5) - vertices[i + j].V) / 256.0);
                        Assert.Equal(c * 256.0, (cx / 0.5) - vertices[i + j].U, 2);
                        Assert.Equal(r * 256.0, (cy / 0.5) - vertices[i + j].V, 2);
                        Assert.True(double.IsNaN(column) || column == c);
                        Assert.True(double.IsNaN(row) || row == r);
                        column = c;
                        row = r;
                    }
                }
            }
        }
    }

    [Fact]
    public void Every_wall_vertex_lies_inside_its_trapezoid_on_its_own_side_of_the_miter()
    {
        var map = Map(CanvasStepSides.All);
        var vertices = Write(Textured(CanvasStepSurface.Walls, map));
        var d = map.Outline;
        var b = map.Inner;
        foreach (var vertex in vertices)
        {
            Assert.InRange(vertex.X, b.X - 0.01, b.Right + 0.01);
            Assert.InRange(vertex.Y, b.Y - 0.01, b.Bottom + 0.01);
            Assert.False(vertex.X > d.X + 0.01 && vertex.X < d.Right - 0.01 && vertex.Y > d.Y + 0.01 && vertex.Y < d.Bottom - 0.01);
        }

        var top = 0;
        foreach (var vertex in vertices)
        {
            if (vertex.Y > d.Y + 0.01)
            {
                continue;
            }

            top++;
            var depth = (d.Y - vertex.Y) / (d.Y - b.Y);
            var leftMiter = d.X + ((b.X - d.X) * depth);
            var rightMiter = d.Right + ((b.Right - d.Right) * depth);
            Assert.InRange(vertex.X, leftMiter - 0.01, rightMiter + 0.01);
        }

        Assert.True(top > 0);
    }

    [Fact]
    public void The_wall_texture_compresses_toward_the_base()
    {
        var map = Map(CanvasStepSides.Top);
        var vertices = Write(Textured(CanvasStepSurface.Walls, map));
        var d = map.Outline;
        var b = map.Inner;
        var middle = (d.Y + b.Y) / 2.0;
        var depth = CanvasStepSurfaceSource.MaterialDepth(d.Y - b.Y, map.Zoom, map.ShelfScale);
        foreach (var vertex in vertices)
        {
            if (Math.Abs(vertex.Y - middle) < 0.01)
            {
                var expected = CanvasStepSurfaceSource.Foreshorten(0.5, map.Zoom, map.ShelfScale) * depth;
                Assert.Equal(expected % 256.0, vertex.V, 2);
            }
        }

        Assert.Contains(vertices, vertex => Math.Abs(vertex.Y - middle) < 0.01);
    }

    [Fact]
    public void The_floor_stays_in_the_ring_and_its_tiles_are_anchored_at_canvas_zero()
    {
        var map = Map(CanvasStepSides.All);
        var vertices = Write(Textured(CanvasStepSurface.Floor, map));
        var u = map.Outer;
        var b = map.Inner;
        foreach (var vertex in vertices)
        {
            Assert.InRange(vertex.X, u.X - 0.01, u.Right + 0.01);
            Assert.InRange(vertex.Y, u.Y - 0.01, u.Bottom + 0.01);
            Assert.False(vertex.X > b.X + 0.01 && vertex.X < b.Right - 0.01 && vertex.Y > b.Y + 0.01 && vertex.Y < b.Bottom - 0.01);
        }

        var (cornerX, cornerY) = map.ToScreen(CanvasStepPlane.Shelf, -1024, -512);
        Assert.True(cornerX < b.X && cornerY < b.Y);
        Assert.Contains(vertices, vertex =>
            Math.Abs(vertex.X - cornerX) < 0.01 && Math.Abs(vertex.Y - cornerY) < 0.01 && (vertex.U is 0f or 256f) && (vertex.V is 0f or 256f));
    }

    [Fact]
    public void The_floor_darkens_toward_the_screen_edge()
    {
        var map = Map(CanvasStepSides.All);
        var source = Textured(CanvasStepSurface.Floor, map);
        var vertices = Write(source);
        var edge = vertices.Where(vertex => Math.Abs(vertex.X - map.Outer.X) < 0.01 && vertex.Y > map.Inner.Y && vertex.Y < map.Inner.Bottom).ToArray();
        var foot = vertices.Where(vertex => Math.Abs(vertex.X - map.Inner.X) < 0.01 && vertex.Y > map.Inner.Y && vertex.Y < map.Inner.Bottom).ToArray();
        Assert.NotEmpty(edge);
        Assert.NotEmpty(foot);
        Assert.All(foot, vertex => Assert.Equal(source.FloorColor, vertex.Color));
        Assert.All(edge, vertex => Assert.Equal(source.FloorColor.R * 0.65f, vertex.Color.R, 4));
        var corner = vertices.Where(vertex => Math.Abs(vertex.X - map.Outer.X) < 0.01 && Math.Abs(vertex.Y - map.Outer.Y) < 0.01).ToArray();
        Assert.NotEmpty(corner);
        Assert.All(corner, vertex => Assert.Equal(source.FloorColor.R * 0.65f, vertex.Color.R, 4));
    }
}
