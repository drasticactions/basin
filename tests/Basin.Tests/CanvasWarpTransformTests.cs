using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasWarpTransformTests
{
    private static CanvasWarpTransform Transform(int sceneX)
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 500, edgeScale: 0.15);
        var right = new CanvasWarp();
        right.Layout(seam: 1000 - 120, direction: 1, zoneWidth: 120, extension: 500, edgeScale: 0.15);
        return new CanvasWarpTransform { Left = left, Right = right, SceneX = sceneX, CellSize = 16 };
    }

    [Fact]
    public void A_window_in_the_flat_span_is_the_identity_and_one_column()
    {
        var transform = Transform(300);
        var bounds = new Box(0, 0, 200, 100);
        Assert.True(transform.IsIdentityFor(bounds));
        Assert.Equal(6, transform.VertexCount(bounds));
        Assert.Equal(bounds, transform.MapBounds(bounds));

        Span<MeshVertex> vertices = stackalloc MeshVertex[6];
        transform.WriteVertices(bounds, vertices);
        Assert.Equal(0f, vertices[0].X);
        Assert.Equal(200f, vertices[1].X);
        Assert.Equal(0f, vertices[0].U);
        Assert.Equal(200f, vertices[1].U);
    }

    [Fact]
    public void A_window_straddling_the_seam_counts_one_flat_and_ceil_columns_per_cell()
    {
        var transform = Transform(80);
        var bounds = new Box(0, 0, 100, 60);
        Assert.False(transform.IsIdentityFor(bounds));
        Assert.Equal((3 + 1) * 6, transform.VertexCount(bounds));

        var mapped = transform.MapBounds(bounds);
        Assert.Equal(0, mapped.Y);
        Assert.Equal(60, mapped.Height);
        Assert.Equal(100, mapped.Right);
        Assert.True(mapped.X > 0 && mapped.X < 40, $"left edge compressed to {mapped.X}");
    }

    [Fact]
    public void Vertices_carry_source_pixels_and_their_hull_matches_MapBounds()
    {
        var transform = Transform(-300);
        var bounds = new Box(0, 0, 200, 60);
        var count = transform.VertexCount(bounds);
        var vertices = new MeshVertex[count];
        transform.WriteVertices(bounds, vertices);

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minU = float.MaxValue;
        var maxU = float.MinValue;
        foreach (var vertex in vertices)
        {
            minX = Math.Min(minX, vertex.X);
            maxX = Math.Max(maxX, vertex.X);
            minU = Math.Min(minU, vertex.U);
            maxU = Math.Max(maxU, vertex.U);
            Assert.True(vertex.V is 0 or 60);
        }

        Assert.Equal(0f, minU);
        Assert.Equal(200f, maxU);
        var mapped = transform.MapBounds(bounds);
        Assert.Equal(mapped.X, (int)Math.Floor(minX));
        Assert.Equal(mapped.Right, (int)Math.Ceiling(maxX));
        Assert.True(mapped.Width < 200, $"a parked window draws narrower: {mapped.Width}");
        Assert.True(mapped.X + (-300) >= 0, "the deformed content lands inside the output");
    }

    [Fact]
    public void TryMapToSource_inverts_the_vertex_map_at_every_column_edge()
    {
        var transform = Transform(-300);
        var bounds = new Box(0, 0, 200, 60);
        var count = transform.VertexCount(bounds);
        var vertices = new MeshVertex[count];
        transform.WriteVertices(bounds, vertices);
        for (var i = 0; i < count; i++)
        {
            var vertex = vertices[i];
            if (vertex.U >= 200 || vertex.V >= 60)
            {
                continue;
            }

            var inside = transform.TryMapToSource(bounds, vertex.X, vertex.Y, out var sourceX, out var sourceY);
            Assert.True(Math.Abs(sourceX - vertex.U) < 0.05, $"vertex {i}: x {vertex.X} -> {sourceX}, u {vertex.U}");
            Assert.True(inside || vertex.U == 0, $"vertex {i}: x {vertex.X} -> {sourceX} is off the content");
            Assert.Equal(vertex.V, sourceY, 6);
        }

        Assert.False(transform.TryMapToSource(bounds, -1, 10, out _, out _));
        Assert.False(transform.TryMapToSource(bounds, 10, 61, out _, out _));
    }

    [Fact]
    public void A_sloped_zone_fans_the_vertices_and_the_inverse_follows()
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 500, edgeScale: 0.15, slope: 0.3, center: 400);
        var transform = new CanvasWarpTransform { Left = left, SceneX = -300, SceneY = 100, CellSize = 16 };
        var bounds = new Box(0, 0, 200, 60);
        var mapped = transform.MapBounds(bounds);
        Assert.True(mapped.Y < 0, $"the top edge rises toward the pivot: {mapped.Y}");
        Assert.True(mapped.Bottom < 60, $"the bottom edge rises too: {mapped.Bottom}");

        var count = transform.VertexCount(bounds);
        var vertices = new MeshVertex[count];
        transform.WriteVertices(bounds, vertices);
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        for (var i = 0; i < count; i++)
        {
            var vertex = vertices[i];
            minY = Math.Min(minY, vertex.Y);
            maxY = Math.Max(maxY, vertex.Y);
            if (vertex.U >= 200 || vertex.U <= 0 || vertex.V >= 60)
            {
                continue;
            }

            var inside = transform.TryMapToSource(bounds, vertex.X, vertex.Y, out var sourceX, out var sourceY);
            Assert.True(inside || vertex.V == 0, $"vertex {i}: ({vertex.X},{vertex.Y}) -> ({sourceX},{sourceY}) is off the content");
            Assert.True(Math.Abs(sourceX - vertex.U) < 0.05, $"vertex {i}: x {vertex.X} -> {sourceX}, u {vertex.U}");
            Assert.True(Math.Abs(sourceY - vertex.V) < 0.05, $"vertex {i}: y {vertex.Y} -> {sourceY}, v {vertex.V}");
        }

        Assert.Equal(mapped.Y, (int)Math.Floor(minY));
        Assert.Equal(mapped.Bottom, (int)Math.Ceiling(maxY));
    }

    [Fact]
    public void A_hit_on_a_fanned_top_row_is_accepted_and_the_fan_ceiling_holds_it_on_screen()
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 500, edgeScale: 0.15, slope: 0.3, center: 400);
        var transform = new CanvasWarpTransform { Left = left, SceneX = -380, SceneY = 30, CellSize = 16 };
        var bounds = new Box(0, -24, 200, 84);

        var mapped = transform.MapBounds(bounds);
        Assert.True(mapped.Y + 30 < 0, $"the fanned top leaves the output at {mapped.Y + 30}");
        var topRow = transform.ToScreenY(-380, 30 - 24) - 30;
        Assert.True(transform.TryMapToSource(bounds, 1, topRow + 1, out _, out var sourceY));
        Assert.True(sourceY >= -24 && sourceY < -20, $"the fanned title row maps back into the title: {sourceY}");
        Assert.False(transform.TryMapToSource(bounds, 1, topRow - 2, out _, out _));

        transform.MaxFan = (400.0 - 0.0) / (400.0 - (30 - 24));
        var clamped = transform.MapBounds(bounds);
        Assert.True(clamped.Y + 30 >= 0, $"the ceiling keeps the title on screen: {clamped.Y + 30}");
        Assert.True(transform.TryMapToSource(bounds, 1, clamped.Y + 1, out _, out var clampedY));
        Assert.True(clampedY >= -24 && clampedY < -20, $"the clamped title row maps back into the title: {clampedY}");
    }

    [Fact]
    public void The_right_zone_is_reached_through_the_same_transform()
    {
        var transform = Transform(1000);
        var bounds = new Box(0, 0, 200, 60);
        Assert.False(transform.IsIdentityFor(bounds));
        var mapped = transform.MapBounds(bounds);
        Assert.True(mapped.Right + 1000 <= 1000, $"the deformed right edge {mapped.Right + 1000} is inside the output");
        Assert.True(transform.TryMapToSource(bounds, mapped.X + 1, 5, out var sourceX, out _));
        Assert.True(sourceX >= 0 && sourceX < 200);
    }
}
