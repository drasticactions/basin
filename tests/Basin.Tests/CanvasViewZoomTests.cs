using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasViewZoomTests
{
    private const double Zoom = 0.75;

    private const double CenterX = 800;

    private const double CenterY = 400;

    private static CanvasWarpTransform Overview(double zoom = Zoom, bool ends = false)
    {
        var shelf = 0.4 / zoom;
        var left = new CanvasWarp();
        left.LayoutTerrace(seam: 0, direction: -1, zoneWidth: 60, shelfWidth: 140, shelfScale: shelf, exponent: 2.0, slope: 0.25, center: CenterY);
        var right = new CanvasWarp(1);
        right.LayoutTerrace(seam: 1600, direction: 1, zoneWidth: 60, shelfWidth: 140, shelfScale: shelf, exponent: 2.0, slope: 0.25, center: CenterY);
        var transform = new CanvasWarpTransform
        {
            Left = left,
            Right = right,
            CellSize = 8,
            ViewScale = zoom,
            ViewCenterX = CenterX,
            ViewCenterY = CenterY,
        };
        if (ends)
        {
            var top = new CanvasWarp();
            top.LayoutTerrace(seam: 0, direction: -1, zoneWidth: 40, shelfWidth: 70, shelfScale: shelf, exponent: 2.0, slope: 0.25, center: CenterX);
            var bottom = new CanvasWarp(1);
            bottom.LayoutTerrace(seam: 800, direction: 1, zoneWidth: 40, shelfWidth: 70, shelfScale: shelf, exponent: 2.0, slope: 0.25, center: CenterX);
            transform.Top = top;
            transform.Bottom = bottom;
        }

        return transform;
    }

    [Fact]
    public void A_view_scale_of_one_changes_nothing_whatever_the_center()
    {
        var plain = Overview(zoom: 1.0, ends: true);
        plain.ViewCenterX = 0;
        plain.ViewCenterY = 0;
        var centered = Overview(zoom: 1.0, ends: true);
        foreach (var (sceneX, sceneY) in new[] { (1500, 300), (-150, 100), (700, -80), (1650, 850) })
        {
            plain.SceneX = centered.SceneX = sceneX;
            plain.SceneY = centered.SceneY = sceneY;
            var bounds = new Box(0, 0, 200, 150);
            Assert.Equal(plain.VertexCount(bounds), centered.VertexCount(bounds));
            var a = new MeshVertex[plain.VertexCount(bounds)];
            var b = new MeshVertex[centered.VertexCount(bounds)];
            plain.WriteVertices(bounds, a);
            centered.WriteVertices(bounds, b);
            Assert.Equal(a, b);
            Assert.Equal(plain.MapBounds(bounds), centered.MapBounds(bounds));
            Assert.Equal(plain.ShelfPlacement(bounds), centered.ShelfPlacement(bounds));
            Assert.Equal(plain.ToScreenPoint(sceneX + 13.5, sceneY + 7.25), centered.ToScreenPoint(sceneX + 13.5, sceneY + 7.25));
        }

        Assert.True(centered.ViewPlacement().IsIdentity);
        Assert.True(centered.IsIdentityFor(new Box(700 - centered.SceneX, 300 - centered.SceneY, 50, 50)));
    }

    [Fact]
    public void A_zoomed_map_round_trips_with_a_pre_scale_at_an_off_center_anchor()
    {
        var transform = Overview(ends: true);
        transform.PreScale = 0.5;
        transform.PreAnchorX = 1690;
        transform.PreAnchorY = 230;
        for (var x = -400; x < 2100; x += 83)
        {
            for (var y = -200; y < 1000; y += 71)
            {
                var (screenX, screenY) = transform.ToScreenPoint(x, y);
                var (backX, backY) = transform.ToCanvasPoint(screenX, screenY);
                Assert.True(Math.Abs(backX - x) < 0.05 && Math.Abs(backY - y) < 0.05, $"({x},{y}) came back as ({backX},{backY})");
            }
        }

        transform.SceneX = 1640;
        transform.SceneY = 180;
        var bounds = new Box(0, 0, 120, 90);
        var vertices = new MeshVertex[transform.VertexCount(bounds)];
        transform.WriteVertices(bounds, vertices);
        foreach (var vertex in vertices)
        {
            _ = transform.TryMapToSource(bounds, vertex.X, vertex.Y, out var u, out var v);
            Assert.True(Math.Abs(u - vertex.U) < 0.05 && Math.Abs(v - vertex.V) < 0.05, $"({vertex.U},{vertex.V}) came back as ({u},{v})");
        }
    }

    [Fact]
    public void The_view_placement_equals_the_mesh_in_the_flat_center()
    {
        var transform = Overview(ends: true);
        transform.SceneX = 400;
        transform.SceneY = 200;
        var bounds = new Box(0, 0, 300, 200);
        Assert.False(transform.IsIdentityFor(bounds));
        var placement = transform.ViewPlacement();
        Assert.Equal(Zoom, placement.M11, 12);
        Assert.Equal(Zoom, placement.M22, 12);
        var vertices = new MeshVertex[transform.VertexCount(bounds)];
        transform.WriteVertices(bounds, vertices);
        Assert.Equal(6, vertices.Length);
        foreach (var vertex in vertices)
        {
            var (x, y) = placement.Map(400 + vertex.U, 200 + vertex.V);
            Assert.True(Math.Abs(x - (400 + vertex.X)) < 1e-3 && Math.Abs(y - (200 + vertex.Y)) < 1e-3, $"({vertex.U},{vertex.V})");
        }

        var (screenX, screenY) = transform.ToScreenPoint(CenterX, CenterY);
        Assert.Equal(CenterX, screenX, 9);
        Assert.Equal(CenterY, screenY, 9);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.7)]
    public void The_shelf_placement_equals_the_mesh_past_the_feet_under_a_zoom(double preScale)
    {
        foreach (var (sceneX, sceneY, width, height) in new[] { (1850, 300, 120, 90), (-300, 100, 120, 300) })
        {
            var transform = Overview();
            transform.SceneX = sceneX;
            transform.SceneY = sceneY;
            transform.PreScale = preScale;
            transform.PreAnchorX = sceneX + (width / 3.0);
            transform.PreAnchorY = sceneY + (height / 4.0);
            var bounds = new Box(0, 0, width, height);
            Assert.True(transform.IsPastFeet(bounds), $"the box at {sceneX},{sceneY} is not past the feet");
            var placement = transform.ShelfPlacement(bounds);
            Assert.Equal(0.4 * preScale, placement.M11, 9);
            var vertices = new MeshVertex[transform.VertexCount(bounds)];
            transform.WriteVertices(bounds, vertices);
            foreach (var vertex in vertices)
            {
                var (x, y) = placement.Map(sceneX + vertex.U, sceneY + vertex.V);
                Assert.True(
                    Math.Abs(x - (sceneX + vertex.X)) < 1e-3 && Math.Abs(y - (sceneY + vertex.Y)) < 1e-3,
                    $"({vertex.U},{vertex.V}) meshes to ({sceneX + vertex.X},{sceneY + vertex.Y}) but places at ({x},{y})");
            }
        }
    }

    [Fact]
    public void The_screen_derivative_is_the_zoom_on_both_sides_of_the_seam()
    {
        var transform = Overview();
        const double step = 0.05;
        foreach (var (seam, outward) in new[] { (0.0, -1.0), (1600.0, 1.0) })
        {
            var at = transform.ToScreenPoint(seam, 400).X;
            var flat = (at - transform.ToScreenPoint(seam - (outward * step), 400).X) / step;
            var slope = (transform.ToScreenPoint(seam + (outward * step), 400).X - at) / step;
            Assert.True(Math.Abs(outward * flat - Zoom) < 1e-3, $"the flat side of {seam} has {flat}");
            Assert.True(Math.Abs(outward * slope - Zoom) < 1e-3, $"the slope side of {seam} has {slope}");
        }
    }

    [Fact]
    public void An_identity_layout_at_no_zoom_maps_every_point_to_itself()
    {
        var left = new CanvasWarp();
        left.LayoutTerrace(seam: 0, direction: -1, zoneWidth: 0, shelfWidth: 0, shelfScale: 1.0, exponent: 2.0, slope: 0.25, center: CenterY);
        var right = new CanvasWarp(1);
        right.LayoutTerrace(seam: 1600, direction: 1, zoneWidth: 0, shelfWidth: 0, shelfScale: 1.0, exponent: 2.0, slope: 0.25, center: CenterY);
        var transform = new CanvasWarpTransform { Left = left, Right = right, ViewScale = 1.0, ViewCenterX = CenterX, ViewCenterY = CenterY };
        for (var x = -900; x < 2600; x += 131)
        {
            Assert.Equal((x, 300.0), transform.ToScreenPoint(x, 300));
        }

        Assert.True(transform.IsIdentityFor(new Box(1700, 0, 50, 50)));
    }

    [Fact]
    public void The_warp_frame_helpers_invert_the_zoom()
    {
        var transform = Overview();
        var (x, y) = transform.Zoom(1600, 0);
        Assert.Equal(CenterX + ((1600 - CenterX) * Zoom), x, 9);
        Assert.Equal(CenterY - (CenterY * Zoom), y, 9);
        var (backX, backY) = transform.Unzoom(x, y);
        Assert.Equal(1600, backX, 9);
        Assert.Equal(0, backY, 9);
        var (canvasX, canvasY) = transform.FromWarpPoint(1600 + 60, 400);
        var screen = transform.ToScreenPoint(canvasX, canvasY);
        Assert.Equal(transform.Zoom(1660, 400).X, screen.X, 6);
    }

    [Fact]
    public void A_zoomed_grid_writes_the_same_lines_moved_to_the_zoomed_frame()
    {
        var left = new CanvasWarp();
        left.LayoutTerrace(seam: 0, direction: -1, zoneWidth: 60, shelfWidth: 140, shelfScale: 0.5, exponent: 2.0, slope: 0.25, center: CenterY);
        var right = new CanvasWarp(1);
        right.LayoutTerrace(seam: 1600, direction: 1, zoneWidth: 60, shelfWidth: 140, shelfScale: 0.5, exponent: 2.0, slope: 0.25, center: CenterY);
        var plain = new CanvasGridSource { CellSize = 64, Left = left, Right = right };
        var bounds = new Box(0, 0, 1600, 800);
        var zoomed = new CanvasGridSource
        {
            CellSize = 64, Left = left, Right = right, ViewScale = 1.0, ViewCenterX = CenterX, ViewCenterY = CenterY,
        };
        Assert.Equal(plain.VertexCount(bounds), zoomed.VertexCount(bounds));
        var a = new MeshVertex[plain.VertexCount(bounds)];
        var b = new MeshVertex[zoomed.VertexCount(bounds)];
        plain.WriteVertices(bounds, a);
        zoomed.WriteVertices(bounds, b);
        Assert.Equal(a, b);

        zoomed.ViewScale = Zoom;
        var count = zoomed.VertexCount(bounds);
        Assert.True(count > a.Length, "a zoomed grid reaches more canvas lines");
        var vertices = new MeshVertex[count];
        zoomed.WriteVertices(bounds, vertices);
        var seamX = (float)(CenterX + ((1600 - CenterX) * Zoom));
        Assert.Contains(vertices, vertex => Math.Abs(vertex.X - seamX) < 0.01f);
        Assert.Contains(vertices, vertex => Math.Abs(vertex.X - (float)(CenterX - (CenterX * Zoom))) < 0.01f);
    }
}
