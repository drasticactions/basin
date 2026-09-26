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
        Assert.True(transform.TryMapToSource(bounds, 381, topRow + 1, out _, out var sourceY));
        Assert.True(sourceY >= -24 && sourceY < -20, $"the fanned title row maps back into the title: {sourceY}");
        Assert.False(transform.TryMapToSource(bounds, 381, topRow - 2, out _, out _));

        transform.MaxFan = (400.0 - 0.0) / (400.0 - (30 - 24));
        var clamped = transform.MapBounds(bounds);
        Assert.True(clamped.Y + 30 >= 0, $"the ceiling keeps the title on screen: {clamped.Y + 30}");
        Assert.True(transform.TryMapToSource(bounds, 381, clamped.Y + 1, out _, out var clampedY));
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

    private static CanvasWarpTransform Sides(int mask, int sceneX, int sceneY)
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

        return new CanvasWarpTransform
        {
            Left = Warp(1, 120, -1, 120, 500, 400),
            Right = Warp(2, 880, 1, 120, 500, 400),
            Top = Warp(4, 96, -1, 96, 400, 500),
            Bottom = Warp(8, 704, 1, 96, 400, 500),
            SceneX = sceneX,
            SceneY = sceneY,
            CellSize = 16,
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
    public void The_two_stage_map_round_trips_in_every_zone_and_corner(int mask)
    {
        var transform = Sides(mask, 0, 0);
        var xs = new[] { -300.0, -60.0, 20.0, 119.0, 121.0, 300.0, 500.0, 879.0, 881.0, 950.0, 1200.0 };
        var ys = new[] { -250.0, -40.0, 30.0, 95.0, 97.0, 250.0, 400.0, 703.0, 705.0, 760.0, 1000.0 };
        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                if (PastTheRim(transform, x, y))
                {
                    continue;
                }

                var (screenX, screenY) = transform.ToScreenPoint(x, y);
                var (backX, backY) = transform.ToCanvasPoint(screenX, screenY);
                Assert.True(
                    Math.Abs(backX - x) < 0.5 && Math.Abs(backY - y) < 0.5,
                    $"mask {mask}: ({x},{y}) -> ({screenX:F2},{screenY:F2}) -> ({backX:F2},{backY:F2})");
            }
        }
    }

    private static bool PastTheRim(CanvasWarpTransform transform, double x, double y)
    {
        static double Depth(CanvasWarp? warp, double canvas) =>
            warp is { IsIdentity: false } && warp.ContainsCanvas(canvas) ? warp.Direction * (canvas - warp.Seam) / warp.Extension : 0.0;

        var ux = Math.Max(Depth(transform.Left, x), Depth(transform.Right, x));
        var uy = Math.Max(Depth(transform.Top, y), Depth(transform.Bottom, y));
        return ux >= 0.999 || uy >= 0.999 || (ux > 0 && uy > 0 && Math.Sqrt((ux * ux) + (uy * uy)) >= 0.98);
    }

    [Theory]
    [MemberData(nameof(SideMasks))]
    public void The_corner_is_a_ring_that_meets_both_zones(int mask)
    {
        var transform = Sides(mask | 5, 0, 0);
        foreach (var t in new[] { 0.1, 0.4, 0.7, 0.95 })
        {
            var x = 120 - (t * 500);
            var y = 96 - (t * 400);
            var alongSide = transform.ToScreenPoint(x, 96.0001);
            var intoCorner = transform.ToScreenPoint(x, 96 - 0.001);
            Assert.True(Math.Abs(alongSide.X - intoCorner.X) < 0.05 && Math.Abs(alongSide.Y - intoCorner.Y) < 0.05, $"the side row meets the corner at {t}");
            var alongTop = transform.ToScreenPoint(120.0001, y);
            var intoCornerTop = transform.ToScreenPoint(120 - 0.001, y);
            Assert.True(Math.Abs(alongTop.X - intoCornerTop.X) < 0.05 && Math.Abs(alongTop.Y - intoCornerTop.Y) < 0.05, $"the top column meets the corner at {t}");
        }

        var rim = transform.ToScreenPoint(120 - (500 / Math.Sqrt(2)), 96 - (400 / Math.Sqrt(2)));
        Assert.True(rim.X >= 0 && rim.Y >= 0, $"the rim at the diagonal stays on screen: {rim}");
        var past = transform.ToScreenPoint(120 - 500, 96 - 400);
        Assert.True(past.X < rim.X && past.Y < rim.Y, $"the canvas past the rim keeps going toward the screen corner: {past} against {rim}");
        var back = transform.ToCanvasPoint(past.X, past.Y);
        Assert.True(Math.Abs(back.X - (120 - 500)) < 0.5 && Math.Abs(back.Y - (96 - 400)) < 0.5, $"and inverts: {back}");
    }

    [Theory]
    [MemberData(nameof(SideMasks))]
    public void MapBounds_contains_every_vertex_and_the_count_matches_the_write(int mask)
    {
        var transform = Sides(mask, -250, -200);
        var bounds = new Box(0, 0, 520, 380);
        var count = transform.VertexCount(bounds);
        Assert.Equal(0, count % 6);
        var vertices = new MeshVertex[count];
        transform.WriteVertices(bounds, vertices);
        var mapped = transform.MapBounds(bounds);
        var minU = float.MaxValue;
        var maxU = float.MinValue;
        var minV = float.MaxValue;
        var maxV = float.MinValue;
        foreach (var vertex in vertices)
        {
            Assert.True(
                vertex.X >= mapped.X && vertex.X <= mapped.Right && vertex.Y >= mapped.Y && vertex.Y <= mapped.Bottom,
                $"mask {mask}: vertex ({vertex.X},{vertex.Y}) is outside {mapped}");
            minU = Math.Min(minU, vertex.U);
            maxU = Math.Max(maxU, vertex.U);
            minV = Math.Min(minV, vertex.V);
            maxV = Math.Max(maxV, vertex.V);
        }

        Assert.Equal((0f, 520f, 0f, 380f), (minU, maxU, minV, maxV));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Without_top_and_bottom_the_vertices_are_the_single_axis_map(int mask)
    {
        var transform = Sides(mask, -300, 100);
        var bounds = new Box(0, 0, 200, 60);
        var count = transform.VertexCount(bounds);
        var vertices = new MeshVertex[count];
        transform.WriteVertices(bounds, vertices);
        foreach (var vertex in vertices)
        {
            var canvasX = vertex.U - 300;
            var canvasY = vertex.V + 100;
            Assert.Equal((float)(transform.ToScreen(canvasX) + 300), vertex.X);
            Assert.Equal((float)(transform.ToScreenY(canvasX, canvasY) - 100), vertex.Y);
        }

        var flat = new MeshVertex[count];
        var withIdentity = Sides(mask, -300, 100);
        withIdentity.Top = new CanvasWarp();
        withIdentity.Bottom = new CanvasWarp(1);
        Assert.Equal(count, withIdentity.VertexCount(bounds));
        withIdentity.WriteVertices(bounds, flat);
        Assert.Equal(vertices, flat);
    }

    [Fact]
    public void A_window_in_the_flat_rows_of_the_side_zone_is_one_row()
    {
        var transform = Sides(15, -300, 300);
        var bounds = new Box(0, 0, 200, 100);
        Assert.Equal(((200 + 15) / 16) * 6, transform.VertexCount(bounds));
    }

    [Fact]
    public void A_corner_window_subdivides_both_axes()
    {
        var transform = Sides(5, -300, -250);
        var bounds = new Box(0, 0, 200, 150);
        var columns = (200 + 15) / 16;
        var rows = (150 + 15) / 16;
        Assert.Equal(columns * rows * 6, transform.VertexCount(bounds));
    }

    [Theory]
    [InlineData(300, 300, true)]
    [InlineData(300, 50, false)]
    [InlineData(300, 650, false)]
    [InlineData(50, 300, false)]
    [InlineData(800, 300, false)]
    public void IsIdentityFor_is_true_only_for_a_box_flat_on_both_axes(int x, int y, bool identity)
    {
        var transform = Sides(15, 0, 0);
        Assert.Equal(identity, transform.IsIdentityFor(new Box(x, y, 100, 100)));
    }

    [Fact]
    public void The_column_fan_cap_holds_in_both_directions()
    {
        var transform = Sides(4, 0, 0);
        var free = transform.ToScreenPoint(900, -300);
        transform.MaxColumnFan = 1.05;
        var capped = transform.ToScreenPoint(900, -300);
        Assert.True(capped.X < free.X, $"the cap narrows the fan: {capped.X} against {free.X}");
        Assert.Equal(free.Y, capped.Y);
        Assert.Equal(900 - 500, (capped.X - 500) / 1.05, 6);
        var (backX, backY) = transform.ToCanvasPoint(capped.X, capped.Y);
        Assert.True(Math.Abs(backX - 900) < 0.5 && Math.Abs(backY + 300) < 0.5, $"({backX},{backY})");
    }

    [Fact]
    public void The_row_fan_cap_holds_in_both_directions()
    {
        var transform = Sides(1, 0, 0);
        transform.MaxFan = 1.1;
        var (x, y) = transform.ToScreenPoint(-300, 60);
        var (backX, backY) = transform.ToCanvasPoint(x, y);
        Assert.True(Math.Abs(backX + 300) < 0.5 && Math.Abs(backY - 60) < 0.5, $"({backX},{backY})");
        Assert.Equal(400 + ((60 - 400) * 1.1), transform.ToScreenY(-300, 60), 6);
    }

    public static TheoryData<double> CornerRadii() => new() { 0.0, 0.3, Math.Sqrt(2) / (1 + Math.Sqrt(2)), 0.75, 1.0 };

    [Theory]
    [MemberData(nameof(CornerRadii))]
    public void Every_corner_radius_round_trips_and_meets_both_zones(double radius)
    {
        var transform = Sides(15, 0, 0);
        transform.CornerRadius = radius;
        foreach (var ux in new[] { 0.05, 0.2, 0.45, 0.7, 0.95 })
        {
            foreach (var uy in new[] { 0.05, 0.2, 0.45, 0.7, 0.95 })
            {
                var x = 120 - (ux * 500);
                var y = 96 - (uy * 400);
                if (uy > transform.CornerReach(ux) * 0.98)
                {
                    continue;
                }

                var (screenX, screenY) = transform.ToScreenPoint(x, y);
                var (backX, backY) = transform.ToCanvasPoint(screenX, screenY);
                Assert.True(
                    Math.Abs(backX - x) < 0.5 && Math.Abs(backY - y) < 0.5,
                    $"radius {radius:F3}: ({x:F1},{y:F1}) -> ({screenX:F2},{screenY:F2}) -> ({backX:F2},{backY:F2})");
            }
        }

        foreach (var t in new[] { 0.1, 0.5, 0.9 })
        {
            var x = 120 - (t * 500);
            var y = 96 - (t * 400);
            var side = transform.ToScreenPoint(x, 96.0001);
            var corner = transform.ToScreenPoint(x, 96 - 0.001);
            Assert.True(Math.Abs(side.X - corner.X) < 0.05 && Math.Abs(side.Y - corner.Y) < 0.05, $"radius {radius:F3}: the side row meets the corner at {t}");
            var top = transform.ToScreenPoint(120.0001, y);
            var cornerTop = transform.ToScreenPoint(120 - 0.001, y);
            Assert.True(Math.Abs(top.X - cornerTop.X) < 0.05 && Math.Abs(top.Y - cornerTop.Y) < 0.05, $"radius {radius:F3}: the top column meets the corner at {t}");
        }
    }

    [Fact]
    public void A_square_corner_fills_the_screen_corner_and_creases_on_the_diagonal()
    {
        var transform = Sides(15, 0, 0);
        transform.CornerRadius = 0;
        Assert.Equal(1.0, transform.RimDiagonal, 9);
        Assert.Equal(1.0, transform.CornerReach(0.9), 9);
        var (x, y) = transform.ToScreenPoint(120 - 500, 96 - 400);
        Assert.True(Math.Abs(x) < 0.01 && Math.Abs(y) < 0.01, $"the far canvas corner lands on the screen corner: ({x},{y})");
        var below = transform.ToScreenPoint(120 - 300, 96 - 239);
        var above = transform.ToScreenPoint(120 - 299, 96 - 240);
        var crease = transform.ToScreenPoint(120 - 300, 96 - 240);
        Assert.Equal(crease.X, below.X, 6);
        Assert.Equal(crease.Y, above.Y, 6);

        transform.CornerRadius = 1;
        Assert.Equal(1 / Math.Sqrt(2), transform.RimDiagonal, 9);
        Assert.Equal(Math.Sqrt(1 - (0.6 * 0.6)), transform.CornerReach(0.6), 9);
    }

    [Theory]
    [MemberData(nameof(CornerRadii))]
    public void MapBounds_contains_every_vertex_at_every_corner_radius(double radius)
    {
        var transform = Sides(15, -250, -200);
        transform.CornerRadius = radius;
        var bounds = new Box(0, 0, 520, 380);
        var vertices = new MeshVertex[transform.VertexCount(bounds)];
        transform.WriteVertices(bounds, vertices);
        var mapped = transform.MapBounds(bounds);
        foreach (var vertex in vertices)
        {
            Assert.True(
                vertex.X >= mapped.X && vertex.X <= mapped.Right && vertex.Y >= mapped.Y && vertex.Y <= mapped.Bottom,
                $"radius {radius:F3}: vertex ({vertex.X},{vertex.Y}) is outside {mapped}");
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.6)]
    [InlineData(0.0)]
    public void A_tapered_corner_round_trips_and_reaches_the_screen_corner(double radius)
    {
        var transform = Sides(15, 0, 0);
        transform.CornerRadius = radius;
        transform.CornerTaper = true;
        Assert.Equal(1.0, transform.RimDiagonal, 9);
        Assert.Equal(1.0, transform.CornerReach(0.9), 9);
        var (farX, farY) = transform.ToScreenPoint(120 - 500, 96 - 400);
        Assert.True(Math.Abs(farX) < 0.01 && Math.Abs(farY) < 0.01, $"radius {radius}: the far canvas corner lands on the screen corner: ({farX},{farY})");
        foreach (var ux in new[] { 0.05, 0.2, 0.45, 0.7, 0.9 })
        {
            foreach (var uy in new[] { 0.05, 0.2, 0.45, 0.7, 0.9 })
            {
                var x = 120 - (ux * 500);
                var y = 96 - (uy * 400);
                var (screenX, screenY) = transform.ToScreenPoint(x, y);
                var (backX, backY) = transform.ToCanvasPoint(screenX, screenY);
                Assert.True(
                    Math.Abs(backX - x) < 0.5 && Math.Abs(backY - y) < 0.5,
                    $"radius {radius}: ({x:F1},{y:F1}) -> ({screenX:F2},{screenY:F2}) -> ({backX:F2},{backY:F2})");
            }
        }

        var bounds = new Box(0, 0, 520, 380);
        transform.SceneX = -250;
        transform.SceneY = -200;
        var vertices = new MeshVertex[transform.VertexCount(bounds)];
        transform.WriteVertices(bounds, vertices);
        var mapped = transform.MapBounds(bounds);
        foreach (var vertex in vertices)
        {
            Assert.True(vertex.X >= mapped.X && vertex.X <= mapped.Right && vertex.Y >= mapped.Y && vertex.Y <= mapped.Bottom);
        }
    }

    [Fact]
    public void A_tapered_corner_is_round_near_the_seam_and_square_at_the_rim()
    {
        var round = Sides(15, 0, 0);
        var tapered = Sides(15, 0, 0);
        tapered.CornerTaper = true;
        var nearRound = round.ToScreenPoint(120 - (0.05 * 500), 96 - (0.05 * 400));
        var nearTapered = tapered.ToScreenPoint(120 - (0.05 * 500), 96 - (0.05 * 400));
        var farRound = round.ToScreenPoint(120 - (0.6 * 500), 96 - (0.6 * 400));
        var farTapered = tapered.ToScreenPoint(120 - (0.6 * 500), 96 - (0.6 * 400));
        var nearGap = Math.Abs(nearRound.X - nearTapered.X) + Math.Abs(nearRound.Y - nearTapered.Y);
        var farGap = Math.Abs(farRound.X - farTapered.X) + Math.Abs(farRound.Y - farTapered.Y);
        Assert.True(nearGap < farGap, $"the taper departs from the round ring with depth: {nearGap:F2} near, {farGap:F2} far");
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    public void Content_past_the_far_edge_goes_off_screen_instead_of_piling_up(double radius, bool taper)
    {
        var transform = Sides(15, 0, 0);
        transform.CornerRadius = radius;
        transform.CornerTaper = taper;
        var edge = transform.ToScreenPoint(500, 704 + 400);
        var past = transform.ToScreenPoint(500, 704 + 600);
        Assert.Equal(800, edge.Y, 6);
        Assert.Equal(800 + (0.15 * 200), past.Y, 6);
        var corner = transform.ToScreenPoint(880 + 600, 704 + 600);
        Assert.True(corner.X > 1000 || corner.Y > 800, $"a point past both far edges leaves the screen: {corner}");
        var back = transform.ToCanvasPoint(past.X, past.Y);
        Assert.True(Math.Abs(back.X - 500) < 0.5 && Math.Abs(back.Y - (704 + 600)) < 0.5, $"and inverts: {back}");
    }
}
