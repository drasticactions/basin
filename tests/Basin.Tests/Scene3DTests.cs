using System.Numerics;
using Basin.Scene3D;
using Xunit;

namespace Basin.Tests;

public sealed class Scene3DTests
{
    private static readonly float Fov = MathF.PI / 3;

    private static WorldCamera Camera(Vector3 position, float yaw = 0, float pitch = 0) => new()
    {
        Position = position,
        Yaw = yaw,
        Pitch = pitch,
        FieldOfView = Fov,
        Near = 0.05f,
        Far = 100,
    };

    public static TheoryData<int[]> Layouts => new()
    {
        new[] { 0, 0, 1920, 1080, 1920, 0, 1920, 1080 },
        new[] { 0, 0, 1920, 1080, 1920, 28, 1280, 1024, 3200, 0, 2560, 1440 },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_slice_projects_a_point_to_the_same_layout_pixel(int[] boxes)
    {
        var slices = Slices(boxes);
        var union = Union(slices);
        var camera = Camera(new Vector3(0.3f, 1.6f, 2), yaw: 0.2f, pitch: -0.1f);
        var points = new[] { new Vector3(0, 1.2f, -3), new Vector3(-2, 2.5f, -6), new Vector3(1.5f, 0.2f, -4) };
        foreach (var point in points)
        {
            var reference = camera.ViewOf(union, slices[0]);
            Assert.True(reference.TryProject(point, out var x0, out var y0, out _));
            foreach (var slice in slices)
            {
                Assert.True(camera.ViewOf(union, slice).TryProject(point, out var x, out var y, out _));
                Assert.Equal(x0, x, 3);
                Assert.Equal(y0, y, 3);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_ray_through_a_layout_pixel_projects_back_onto_it(int[] boxes)
    {
        var slices = Slices(boxes);
        var union = Union(slices);
        var camera = Camera(new Vector3(0, 1.6f, 0), yaw: -0.4f, pitch: 0.15f);
        foreach (var slice in slices)
        {
            var view = camera.ViewOf(union, slice);
            var targets = new (double X, double Y)[]
            {
                (slice.X + 1, slice.Y + 1),
                (slice.X + (slice.Width / 2.0), slice.Y + (slice.Height / 3.0)),
                (slice.X + slice.Width - 1, slice.Y + slice.Height - 1),
            };
            foreach (var (tx, ty) in targets)
            {
                var ray = view.RayAt(tx, ty);
                Assert.True(view.TryProject(ray.At(7), out var x, out var y, out var depth));
                Assert.Equal(tx, x, 2);
                Assert.Equal(ty, y, 2);
                Assert.InRange(depth, 0f, 1f);
            }
        }
    }

    [Fact]
    public void Two_outputs_side_by_side_meet_without_a_seam()
    {
        var left = new Box(0, 0, 1000, 800);
        var right = new Box(1000, 0, 1000, 800);
        var union = new Box(0, 0, 2000, 800);
        var camera = Camera(Vector3.Zero);
        var seam = camera.ViewOf(union, left).RayAt(1000, 400).At(5);
        Assert.True(camera.ViewOf(union, left).TryProject(seam, out var lx, out _, out _));
        Assert.True(camera.ViewOf(union, right).TryProject(seam, out var rx, out _, out _));
        Assert.Equal(1000, lx, 3);
        Assert.Equal(1000, rx, 3);
        Assert.Equal(0, seam.X, 4);
    }

    [Fact]
    public void The_picker_maps_the_window_corners_to_the_surface_corners()
    {
        var (world, window) = OneWindow(400, 300);
        var picker = new WorldPicker(world);
        var eye = new Vector3(0, 1.6f, 2);
        var corners = new (float X, float Y)[] { (0.5f, 0.5f), (399.5f, 0.5f), (0.5f, 299.5f), (399.5f, 299.5f), (200, 150) };
        foreach (var (sx, sy) in corners)
        {
            var target = window.SurfacePoint(sx, sy);
            var hit = picker.Pick(new WorldRay(eye, Vector3.Normalize(target - eye)));
            Assert.Equal(WorldHitKind.Window, hit.Kind);
            Assert.Same(window, hit.Window);
            Assert.Equal(sx, hit.SurfaceX, 3);
            Assert.Equal(sy, hit.SurfaceY, 3);
        }
    }

    [Fact]
    public void Past_the_edge_the_grab_plane_extends_the_surface_coordinates()
    {
        var (_, window) = OneWindow(400, 300);
        var eye = new Vector3(0, 1.6f, 2);
        var beyond = window.SurfacePoint(-50, 350);
        var ray = new WorldRay(eye, Vector3.Normalize(beyond - eye));
        Assert.False(WorldPicker.TryWindow(window, ray, out var direct) && direct.Kind != WorldHitKind.None && direct.Layer >= 0);
        Assert.True(WorldPicker.TryOnPlane(window, 0, ray, out var sx, out var sy));
        Assert.Equal(-50, sx, 3);
        Assert.Equal(350, sy, 3);
    }

    [Fact]
    public void A_popup_layer_wins_over_the_window_under_it()
    {
        var (world, window) = OneWindow(400, 300);
        window.AddLayer(new WorldSurfaceLayer { X = 100, Y = 80, Width = 120, Height = 90, Level = 1 });
        var eye = new Vector3(0, 1.6f, 2);
        var target = window.SurfacePoint(130, 100, WorldPicker.LayerLift);
        var hit = new WorldPicker(world).Pick(new WorldRay(eye, Vector3.Normalize(target - eye)));
        Assert.Equal(1, hit.Layer);
        Assert.Equal(30, hit.SurfaceX, 2);
        Assert.Equal(20, hit.SurfaceY, 2);
    }

    [Fact]
    public void A_wall_between_the_eye_and_a_window_blocks_the_pick()
    {
        var (world, window) = OneWindow(400, 300);
        world.Rooms[0].Blockers.Add(new WorldBounds(new Vector3(-3, 0, 0.5f), new Vector3(3, 3, 0.8f)));
        var eye = new Vector3(0, 1.6f, 2);
        var target = window.SurfacePoint(200, 150);
        var hit = new WorldPicker(world).Pick(new WorldRay(eye, Vector3.Normalize(target - eye)));
        Assert.Equal(WorldHitKind.Geometry, hit.Kind);
        Assert.Equal(1.2f, hit.Distance, 2);
    }

    [Fact]
    public void A_tilted_body_still_maps_to_exact_surface_coordinates()
    {
        var (world, window) = OneWindow(640, 480);
        window.Orientation = Quaternion.CreateFromYawPitchRoll(0.5f, -0.3f, 0.2f);
        window.Position = new Vector3(0.4f, 0.6f, -1.5f);
        var picker = new WorldPicker(world);
        var eye = new Vector3(0, 1.6f, 1);
        foreach (var (sx, sy) in new (float, float)[] { (10, 12), (320, 240), (600, 470) })
        {
            var target = window.SurfacePoint(sx, sy);
            var hit = picker.Pick(new WorldRay(eye, Vector3.Normalize(target - eye)));
            Assert.Equal(0, hit.Layer);
            Assert.Equal(sx, hit.SurfaceX, 2);
            Assert.Equal(sy, hit.SurfaceY, 2);
        }
    }

    [Fact]
    public void From_behind_a_window_blocks_without_giving_a_surface()
    {
        var (world, window) = OneWindow(400, 300);
        var eye = new Vector3(0, 1.6f, -4);
        var hit = new WorldPicker(world).Pick(new WorldRay(eye, Vector3.Normalize(window.Position - eye)));
        Assert.Equal(WorldHitKind.Window, hit.Kind);
        Assert.Equal(-1, hit.Layer);
        Assert.Null(hit.Surface);
    }

    [Fact]
    public void Visibility_walks_through_a_doorway_the_eye_can_see()
    {
        var world = new World();
        var a = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-3, 0, -3), new Vector3(3, 3, 3)));
        var b = new WorldRoom(1, WorldRoomKind.Room, new WorldBounds(new Vector3(-3, 0, -9), new Vector3(3, 3, -3)));
        var link = new WorldDoorway(a, b, new Vector3(0, 1.2f, -3), -Vector3.UnitZ, 1.5f, 2.4f);
        a.Doorways.Add(link);
        b.Doorways.Add(link);
        world.Rooms.Add(a);
        world.Rooms.Add(b);
        var union = new Box(0, 0, 1600, 900);
        var set = new WorldVisibleSet();

        WorldVisibility.Collect(world, Camera(new Vector3(0, 1.6f, 1)).ViewOf(union, union), 3, set);
        Assert.Equal([a, b], set.Rooms);
        Assert.Same(a, set.EyeRoom);

        set.Clear();
        WorldVisibility.Collect(world, Camera(new Vector3(0, 1.6f, 1), yaw: MathF.PI).ViewOf(union, union), 3, set);
        Assert.Equal([a], set.Rooms);

        set.Clear();
        WorldVisibility.Collect(world, Camera(new Vector3(0, 1.6f, 1)).ViewOf(union, union), 0, set);
        Assert.Equal([a], set.Rooms);
    }

    [Fact]
    public void The_oblique_near_plane_clips_what_is_behind_the_target_portal()
    {
        var world = new World();
        var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-10), new Vector3(10)));
        var here = new WorldPortal(room, new Vector3(0, 1.5f, -2), 0, 1.5f, 2.5f);
        var there = new WorldPortal(room, new Vector3(20, 1.5f, 0), MathF.PI / 2, 1.5f, 2.5f);
        here.Target = there;
        world.Rooms.Add(room);
        var union = new Box(0, 0, 1600, 900);
        var view = Camera(new Vector3(0, 1.6f, 1)).ViewOf(union, union);
        var through = view.Through(here.TransferTo(there), there.Plane);

        var eyeThrough = through.Eye;
        Assert.Equal(20 - 3, eyeThrough.X, 3);
        Assert.Equal(0, eyeThrough.Z, 3);

        var inFront = there.Center + (there.Normal * 2);
        var behind = there.Center - (there.Normal * 0.5f);
        Assert.True(Clip(through, inFront, out var frontDepth));
        Assert.InRange(frontDepth, 0f, 1f);
        Clip(through, behind, out var behindDepth);
        Assert.True(behindDepth < 0, $"depth behind the portal was {behindDepth}");
    }

    [Fact]
    public void A_pose_moves_the_skinned_joint_with_its_parent()
    {
        var skeleton = new Skeleton(
            ["root", "arm"],
            [-1, 0],
            [Vector3.Zero, new Vector3(0, 1, 0)],
            [Quaternion.Identity, Quaternion.Identity],
            [Vector3.One, Vector3.One],
            [0, 1],
            [Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, -1, 0)]);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var clip = new SkeletonClip("turn", 1, 1, [0], [Vector3.Zero, Vector3.Zero], [Quaternion.Identity, turn], [Vector3.One, Vector3.One]);
        var pose = new SkeletonPose(skeleton);

        pose.Reset();
        pose.Apply(clip, 1, 1, loop: false);
        pose.Resolve();

        var tip = Vector3.Transform(new Vector3(0, 2, 0), pose.Palette[1]);
        Assert.Equal(-2, tip.X, 4);
        Assert.Equal(0, tip.Y, 4);

        pose.Reset();
        pose.Apply(clip, 0.5f, 1, loop: false);
        pose.Resolve();
        var half = Vector3.Transform(new Vector3(0, 1, 0), pose.Palette[0]);
        Assert.Equal(-MathF.Sqrt(0.5f), half.X, 4);
    }

    [Fact]
    public void Picking_and_collecting_allocate_nothing()
    {
        var (world, window) = OneWindow(400, 300);
        window.AddLayer(new WorldSurfaceLayer { X = 10, Y = 10, Width = 50, Height = 50, Level = 1 });
        world.Rooms[0].Blockers.Add(new WorldBounds(new Vector3(-3, 0, -2), new Vector3(3, 3, -1.8f)));
        var picker = new WorldPicker(world);
        var union = new Box(0, 0, 1600, 900);
        var camera = Camera(new Vector3(0, 1.6f, 2));
        var set = new WorldVisibleSet();
        var view = camera.ViewOf(union, union);
        WorldVisibility.Collect(world, view, 3, set);
        _ = picker.Pick(view.RayAt(800, 450), set);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            set.Clear();
            WorldVisibility.Collect(world, view, 3, set);
            _ = picker.Pick(view.RayAt(700 + (i % 200), 450), set);
            _ = WorldPicker.TryOnPlane(window, 0, view.RayAt(10, 10), out _, out _);
        }

        var measured = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, measured);
        if (Budgets.Recorded)
        {
            Budgets.Check("dungeon-wm", "pick", measured);
        }
    }

    private static bool Clip(in WorldView view, Vector3 point, out float depth)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), view.ViewProjection);
        depth = clip.Z / clip.W;
        return clip.W > 0;
    }

    private static (World World, WorldWindow Window) OneWindow(int width, int height)
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
        };
        window.AddLayer(new WorldSurfaceLayer { Width = width, Height = height, Level = 0, Input = true });
        world.Windows.Add(window);
        return (world, window);
    }

    private static Box[] Slices(int[] boxes)
    {
        var result = new Box[boxes.Length / 4];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Box(boxes[i * 4], boxes[(i * 4) + 1], boxes[(i * 4) + 2], boxes[(i * 4) + 3]);
        }

        return result;
    }

    private static Box Union(Box[] slices)
    {
        var x0 = slices.Min(s => s.X);
        var y0 = slices.Min(s => s.Y);
        var x1 = slices.Max(s => s.X + s.Width);
        var y1 = slices.Max(s => s.Y + s.Height);
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }
}
