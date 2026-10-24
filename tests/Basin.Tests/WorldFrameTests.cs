using System.Numerics;
using Basin.Scene3D;
using Xunit;

namespace Basin.Tests;

public sealed class WorldFrameTests
{
    private static readonly Vector3 Eye = new(0, 1.6f, 2);

    [Fact]
    public void The_moulding_spans_the_rails_and_reaches_back_to_the_wall()
    {
        var mesh = WorldPrimitives.Moulding(0.4f, 0.3f, 0.06f, 0.04f, 0.015f, new Vector4(0.5f, 0, 0, 0.25f), null);
        Assert.Equal(-0.26f, mesh.Bounds.Min.X, 5);
        Assert.Equal(0.26f, mesh.Bounds.Max.X, 5);
        Assert.Equal(-0.21f, mesh.Bounds.Min.Y, 5);
        Assert.Equal(0.21f, mesh.Bounds.Max.Y, 5);
        Assert.Equal(-0.04f, mesh.Bounds.Min.Z, 5);
        Assert.Equal(0, mesh.Bounds.Max.Z, 5);
    }

    [Fact]
    public void Every_moulding_face_winds_toward_its_normal_and_faces_out_of_the_opening()
    {
        var mesh = WorldPrimitives.Moulding(0.4f, 0.3f, 0.06f, 0.04f, 0.015f, new Vector4(0.5f, 0, 0, 0.25f), null);
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];
            var winding = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Assert.True(Vector3.Dot(winding, a.Normal) > 0, $"triangle {i / 3} winds away from its normal");
            Assert.Equal(1, a.Normal.Length(), 4);
            Assert.True(a.Normal.Z > -1e-4f, $"triangle {i / 3} faces the wall");
        }
    }

    [Fact]
    public void The_canvas_is_recessed_and_still_maps_exactly_to_surface_coordinates()
    {
        var (world, window) = FramedWindow(400, 300);
        var picker = new WorldPicker(world);
        foreach (var (sx, sy) in new (float, float)[] { (0.5f, 0.5f), (399.5f, 0.5f), (0.5f, 299.5f), (399.5f, 299.5f), (200, 150) })
        {
            var target = window.SurfacePoint(sx, sy);
            Assert.Equal(-0.015f, Vector3.Transform(target, Inverse(window)).Z, 5);
            var hit = picker.Pick(new WorldRay(Eye, Vector3.Normalize(target - Eye)));
            Assert.Equal(WorldHitKind.Window, hit.Kind);
            Assert.Equal(0, hit.Layer);
            Assert.Equal(sx, hit.SurfaceX, 3);
            Assert.Equal(sy, hit.SurfaceY, 3);
        }
    }

    [Fact]
    public void The_grab_plane_is_the_recessed_canvas()
    {
        var (_, window) = FramedWindow(400, 300);
        var beyond = window.SurfacePoint(-50, 350);
        Assert.True(WorldPicker.TryOnPlane(window, 0, new WorldRay(Eye, Vector3.Normalize(beyond - Eye)), out var sx, out var sy));
        Assert.Equal(-50, sx, 3);
        Assert.Equal(350, sy, 3);
    }

    [Theory]
    [InlineData(-0.23f, 0.0f, WorldFramePart.Face)]
    [InlineData(0.205f, 0.0f, WorldFramePart.Right)]
    [InlineData(-0.205f, 0.05f, WorldFramePart.Left)]
    [InlineData(0.0f, 0.155f, WorldFramePart.Top)]
    [InlineData(0.1f, -0.155f, WorldFramePart.Bottom)]
    [InlineData(0.0f, 0.2f, WorldFramePart.Face)]
    [InlineData(0.23f, 0.18f, WorldFramePart.TopRight)]
    [InlineData(-0.23f, 0.18f, WorldFramePart.TopLeft)]
    [InlineData(0.23f, -0.18f, WorldFramePart.BottomRight)]
    [InlineData(-0.23f, -0.18f, WorldFramePart.BottomLeft)]
    public void A_press_on_the_frame_names_the_rail_or_corner_under_it(float x, float y, WorldFramePart part)
    {
        var (world, window) = FramedWindow(400, 300);
        var target = Vector3.Transform(new Vector3(x, y, 0), window.Model);
        var hit = new WorldPicker(world).Pick(new WorldRay(Eye, Vector3.Normalize(target - Eye)));
        Assert.Equal(WorldHitKind.Frame, hit.Kind);
        Assert.Same(window, hit.Window);
        Assert.Equal(part, hit.Part);
        Assert.Equal(-1, hit.Layer);
    }

    [Fact]
    public void The_plaque_reports_its_own_pixels()
    {
        var (world, window) = FramedWindow(400, 300);
        var frame = window.Frame!;
        var box = frame.PlaqueBox(window.Height);
        var picker = new WorldPicker(world);
        foreach (var (u, v) in new (float, float)[] { (0.1f, 0.2f), (0.5f, 0.5f), (0.9f, 0.8f) })
        {
            var local = new Vector3(box.Min.X + (u * frame.PlaqueWidth), box.Max.Y - (v * frame.PlaqueHeight), box.Max.Z);
            var target = Vector3.Transform(local, window.Model);
            var hit = picker.Pick(new WorldRay(Eye, Vector3.Normalize(target - Eye)));
            Assert.Equal(WorldHitKind.Plaque, hit.Kind);
            Assert.Same(window, hit.Window);
            Assert.Equal(u * frame.PlaquePixelWidth, hit.SurfaceX, 1);
            Assert.Equal(v * frame.PlaquePixelHeight, hit.SurfaceY, 1);
        }
    }

    [Fact]
    public void The_bounds_hold_the_frame_and_the_plaque()
    {
        var (_, window) = FramedWindow(400, 300);
        var local = window.LocalBounds;
        Assert.Equal(-0.26f, local.Min.X, 5);
        Assert.Equal(0.21f, local.Max.Y, 5);
        Assert.Equal(-(0.15f + 0.06f + 0.012f + 0.07f), local.Min.Y, 5);
        Assert.Equal(-0.04f, local.Min.Z, 5);
        Assert.Equal(0.06f + 0.012f + 0.07f + 0.3f + 0.06f, window.Frame!.PaintingHeight(window.Height), 5);
    }

    [Fact]
    public void A_popup_past_the_canvas_sits_on_the_front_of_the_frame()
    {
        var (world, window) = FramedWindow(400, 300);
        window.AddLayer(new WorldSurfaceLayer { X = 300, Y = 250, Width = 150, Height = 120, Level = 1, Input = true });
        Assert.Equal(WorldPicker.LayerLift, window.LayerDepth(window.Layers[1]), 6);
        var local = new Vector3(0.21f, -0.19f, WorldPicker.LayerLift);
        var target = Vector3.Transform(local, window.Model);
        var hit = new WorldPicker(world).Pick(new WorldRay(Eye, Vector3.Normalize(target - Eye)));
        Assert.Equal(1, hit.Layer);
        Assert.Equal(110, hit.SurfaceX, 1);
        Assert.Equal(90, hit.SurfaceY, 1);
    }

    [Fact]
    public void Picking_a_framed_window_allocates_nothing()
    {
        var (world, window) = FramedWindow(400, 300);
        var picker = new WorldPicker(world);
        var ray = new WorldRay(Eye, Vector3.Normalize(Vector3.Transform(new Vector3(0.23f, 0.18f, 0), window.Model) - Eye));
        _ = picker.Pick(ray);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = picker.Pick(ray);
            _ = WorldPicker.TryOnPlane(window, 0, ray, out _, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static Matrix4x4 Inverse(WorldWindow window)
    {
        Matrix4x4.Invert(window.Model, out var inverse);
        return inverse;
    }

    private static (World World, WorldWindow Window) FramedWindow(int width, int height)
    {
        var world = new World();
        var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-5, 0, -5), new Vector3(5, 3, 5)));
        world.Rooms.Add(room);
        var window = new WorldWindow
        {
            Position = new Vector3(0, 1.6f, -1.8f),
            Width = width / 1000f,
            Height = height / 1000f,
            SurfaceWidth = width,
            SurfaceHeight = height,
            Room = room,
            Frame = new WorldWindowFrame
            {
                PlaqueWidth = 0.3f,
                PlaqueHeight = 0.07f,
                PlaquePixelWidth = 600,
                PlaquePixelHeight = 140,
            },
        };
        window.AddLayer(new WorldSurfaceLayer { Width = width, Height = height, Level = 0, Input = true });
        world.Windows.Add(window);
        return (world, window);
    }
}
