using System.Numerics;
using Basin.Diagnostics;
using Basin.Effects;
using Basin.Render.Gl;
using Basin.Render3D.Gl;
using Basin.Render3D.Vulkan;
using Basin.Render.Vulkan;
using Basin.Scene3D;
using Xunit;

namespace Basin.Tests;

public sealed class WorldRendererTests
{
    private const int Width = 320;
    private const int Height = 240;
    private static readonly float Fov = MathF.PI / 3;
    private static readonly Box Full = new(0, 0, Width, Height);

    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_facing_window_draws_upright_and_unmirrored(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Quadrants(64, 64);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World { ClearColor = Vector3.Zero };
        var window = FacingWindow(world, texture, 64, 64, OneToOneDistance());
        window.Focused = true;

        var rgba = Render(host, renderer, world, Camera(), Width, Height);

        Assert.Equal((255, 0, 0), Pixel(rgba, 160 - 16, 120 - 16));
        Assert.Equal((0, 255, 0), Pixel(rgba, 160 + 16, 120 - 16));
        Assert.Equal((0, 0, 255), Pixel(rgba, 160 - 16, 120 + 16));
        Assert.Equal((255, 255, 255), Pixel(rgba, 160 + 16, 120 + 16));
        Assert.Equal((0, 0, 0), Pixel(rgba, 10, 10));
        buffer.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_focused_window_below_full_opacity_draws_blended(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Solid(64, 64, 0xFFFF0000);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World { ClearColor = Vector3.Zero };
        var window = FacingWindow(world, texture, 64, 64, OneToOneDistance());
        window.Focused = true;

        window.Opacity = 0.5f;
        var half = Pixel(Render(host, renderer, world, Camera(), Width, Height), 160, 120);
        window.Opacity = 1;
        window.CanvasOpacity = 0.5f;
        var canvas = Pixel(Render(host, renderer, world, Camera(), Width, Height), 160, 120);
        window.CanvasOpacity = 1;
        var full = Pixel(Render(host, renderer, world, Camera(), Width, Height), 160, 120);

        Assert.Equal((255, 0, 0), full);
        Assert.InRange(half.R, 110, 150);
        Assert.InRange(canvas.R, 110, 150);
        Assert.True(half.G < 20 && half.B < 20 && canvas.G < 20 && canvas.B < 20, $"{half} {canvas}");
        buffer.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_focused_window_agrees_with_the_2d_homography_of_its_corners(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Checker(96, 72);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World { ClearColor = Vector3.Zero };
        var window = FacingWindow(world, texture, 96, 72, OneToOneDistance());
        window.Focused = true;
        window.Orientation = Quaternion.CreateFromYawPitchRoll(0.35f, 0.2f, 0);
        var camera = Camera();
        var view = camera.ViewOf(Full, Full);

        var world3d = Render(host, renderer, world, camera, Width, Height);

        var lift = 0.0001f;
        (double X, double Y) Corner(float sx, float sy)
        {
            Assert.True(view.TryProject(window.SurfacePoint(sx, sy, lift), out var x, out var y, out _));
            return (x, y);
        }

        var destination = new Box(0, 0, 96, 72);
        var transform = Projection.MapRect(destination, Corner(0, 0), Corner(96, 0), Corner(0, 72), Corner(96, 72));
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0, 0, 0, 1), Full);
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = destination, Transform = transform });
        Assert.True(pass.Submit());
        var flat = BufferCapture.ReadRgba(target);
        target.Destroy();

        var inside = 0;
        var differing = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!DeepInside(transform, x + 0.5, y + 0.5, 96, 72, 2.5))
                {
                    continue;
                }

                inside++;
                var i = ((y * Width) + x) * 4;
                var delta = Math.Max(Math.Abs(flat[i] - world3d[i]), Math.Max(Math.Abs(flat[i + 1] - world3d[i + 1]), Math.Abs(flat[i + 2] - world3d[i + 2])));
                if (delta > 1)
                {
                    differing++;
                }
            }
        }

        Assert.True(inside > 3000, $"only {inside} pixels inside");
        Assert.True(differing == 0, $"{differing} of {inside} interior pixels differ from the 2D homography by more than 1");
        buffer.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void An_anisotropy_change_between_frames_applies_and_tears_down_clean(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Checker(256, 256);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World();
        var window = FacingWindow(world, texture, 256, 256, OneToOneDistance() * 3);
        window.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -1.35f);
        window.Width *= 4;
        window.Height *= 4;

        var first = Render(host, renderer, world, Camera(), Width, Height, new WorldRenderSettings { Anisotropy = 1 });
        var second = Render(host, renderer, world, Camera(), Width, Height, new WorldRenderSettings { Anisotropy = 16 });
        var third = Render(host, renderer, world, Camera(), Width, Height, new WorldRenderSettings { Anisotropy = 1 });
        Assert.Equal(first, third);
        Assert.NotEqual(first, second);
        buffer.Destroy();
    }

    [Fact]
    public void The_vulkan_pass_declines_a_target_that_is_not_a_dmabuf()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        using var host = new CompositorTestHost(renderer: "vulkan");
        using var renderer = CreateRenderer(host, "vulkan");
        var world = new World();
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        var box = new Box(0, 0, Width, Height);
        var view = Camera().ViewOf(box, box);
        var settings = new WorldRenderSettings();
        var set = new WorldVisibleSet();
        WorldVisibility.Collect(world, view, settings.RoomDepth, set);

        Assert.False(renderer.Render(new WorldFrame(world, view, new FrameTick(0, 16_666_667), set, settings), target));
        Assert.False(renderer.Render(new WorldFrame(world, view, new FrameTick(16_666_667, 16_666_667), set, settings), target));
        target.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_distant_window_refreshes_its_mip_copy_only_on_new_content(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Checker(256, 256);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World();
        var window = FacingWindow(world, texture, 256, 256, OneToOneDistance() * 4);

        Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal(1, Refreshed(renderer));
        Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal(0, Refreshed(renderer));
        window.EditableLayers[0].ContentVersion++;
        Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal(1, Refreshed(renderer));

        window.Position = new Vector3(0, 0, -OneToOneDistance() * 4 / 4);
        Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal(0, Refreshed(renderer));
        Assert.Equal(1, Direct(renderer));
        buffer.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_nearer_window_wins_whatever_the_draw_order(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var red = Solid(64, 64, 0xFFFF0000);
        var blue = Solid(64, 64, 0xFF0000FF);
        using var redTexture = host.Renderer.ImportTexture(red)!;
        using var blueTexture = host.Renderer.ImportTexture(blue)!;
        var world = new World();
        var near = FacingWindow(world, redTexture, 64, 64, 0.3f);
        var far = FacingWindow(world, blueTexture, 128, 128, 0.6f);
        near.Focused = far.Focused = true;
        world.Windows.Reverse();

        var rgba = Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal((255, 0, 0), Pixel(rgba, 160, 120));

        world.Windows.Reverse();
        rgba = Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal((255, 0, 0), Pixel(rgba, 160, 120));
        red.Destroy();
        blue.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_portal_shows_what_its_target_faces_and_nothing_behind_it(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var green = Solid(64, 64, 0xFF00FF00);
        var red = Solid(64, 64, 0xFFFF0000);
        using var greenTexture = host.Renderer.ImportTexture(green)!;
        using var redTexture = host.Renderer.ImportTexture(red)!;
        var world = new World { ClearColor = new Vector3(0, 0, 0) };
        var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-50), new Vector3(50)));
        world.Rooms.Add(room);
        var here = new WorldPortal(room, new Vector3(0, 0, -1), 0, 0.6f, 0.6f);
        var there = new WorldPortal(room, new Vector3(20, 0, 0), MathF.PI / 2, 0.6f, 0.6f);
        here.Target = there;
        room.Portals.Add(here);

        var seen = FacingWindow(world, greenTexture, 400, 400, 0);
        seen.Position = new Vector3(22, 0, 0);
        seen.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
        seen.Focused = true;
        seen.Room = room;

        var hidden = FacingWindow(world, redTexture, 2000, 2000, 0);
        hidden.Position = new Vector3(19, 0, 0);
        hidden.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
        hidden.Focused = true;
        hidden.Room = room;

        var rgba = Render(host, renderer, world, Camera(), Width, Height);
        Assert.Equal((0, 255, 0), Pixel(rgba, 160, 120));
        Assert.Equal((0, 0, 0), Pixel(rgba, 5, 5));
        green.Destroy();
        red.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_torch_lights_the_window_beside_it(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var white = Solid(64, 64, 0xFFFFFFFF);
        using var texture = host.Renderer.ImportTexture(white)!;
        var world = new World { Ambient = new Vector3(0.1f) };
        var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-5), new Vector3(5)));
        world.Rooms.Add(room);
        var dark = FacingWindow(world, texture, 64, 64, 0.5f);
        dark.Position = new Vector3(-0.1f, 0, -0.5f);
        var lit = FacingWindow(world, texture, 64, 64, 0.5f);
        lit.Position = new Vector3(0.1f, 0, -0.5f);
        dark.Room = lit.Room = room;
        room.Lights.Add(new WorldLight(new Vector3(0.1f, 0, -0.3f), new Vector3(1, 0.8f, 0.6f), 0.5f, 1.5f));

        var rgba = Render(host, renderer, world, Camera(), Width, Height);
        var (darkX, darkY) = ProjectCenter(dark);
        var (litX, litY) = ProjectCenter(lit);
        Assert.True(Pixel(rgba, litX, litY).R > Pixel(rgba, darkX, darkY).R + 60);
        white.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_gilt_frame_catches_a_highlight_a_matte_frame_does_not(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var white = Solid(64, 64, 0xFF202020);
        using var texture = host.Renderer.ImportTexture(white)!;
        var gilt = WorldMaterial.Default with { Albedo = new Vector4(0.8f, 0.6f, 0.2f, 1), SpecularStrength = 1.2f, Shininess = 40 };
        var matte = gilt with { SpecularStrength = 0 };
        var shiny = Sum(RenderFramed(host, renderer, texture, gilt));
        var dull = Sum(RenderFramed(host, renderer, texture, matte));
        Assert.True(shiny > dull * 1.05, $"gilt sums to {shiny}, matte to {dull}");
        white.Destroy();

        static byte[] RenderFramed(CompositorTestHost host, IWorldRenderer renderer, ITexture texture, WorldMaterial material)
        {
            var world = new World { Ambient = new Vector3(0.05f), ClearColor = Vector3.Zero };
            var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-5), new Vector3(5)));
            world.Rooms.Add(room);
            var window = FacingWindow(world, texture, 64, 64, 0.45f);
            window.Room = room;
            window.Frame = Framed(window, null, material);
            room.Lights.Add(new WorldLight(new Vector3(0.02f, 0.02f, -0.3f), new Vector3(1, 0.9f, 0.7f), 1.5f, 1.2f));
            return Render(host, renderer, world, Camera(), Width, Height);
        }

        static long Sum(byte[] rgba)
        {
            var sum = 0L;
            for (var i = 0; i < rgba.Length; i += 4)
            {
                sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
            }

            return sum;
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_framed_canvas_draws_recessed_inside_its_moulding(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Quadrants(64, 64);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World { ClearColor = Vector3.Zero, Ambient = new Vector3(1) };
        var window = FacingWindow(world, texture, 64, 64, OneToOneDistance());
        window.Frame = Framed(window, texture, WorldMaterial.Default with { Albedo = new Vector4(0, 0, 1, 1), SpecularStrength = 0 });
        window.Position += new Vector3(0, 0, window.Frame.Recess);

        var rgba = Render(host, renderer, world, Camera(), Width, Height);

        Assert.Equal((255, 0, 0), Pixel(rgba, 160 - 16, 120 - 16));
        Assert.Equal((255, 255, 255), Pixel(rgba, 160 + 16, 120 + 16));
        var rail = Pixel(rgba, 160 + 32 + 20, 120);
        Assert.True(rail.B > 20 && rail.R < 10 && rail.G < 10, $"the rail beside the canvas is {rail}");
        buffer.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_world_frame_allocates_nothing_over_1000_frames(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var buffer = Checker(128, 96);
        using var texture = host.Renderer.ImportTexture(buffer)!;
        var world = new World();
        var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-50), new Vector3(50)));
        world.Rooms.Add(room);
        room.Lights.Add(new WorldLight(new Vector3(0, 0.3f, -0.4f), Vector3.One, 2, 1, 0.2f, 1));
        var here = new WorldPortal(room, new Vector3(0.3f, 0, -1), 0, 0.3f, 0.3f);
        var there = new WorldPortal(room, new Vector3(20, 0, 0), MathF.PI / 2, 0.3f, 0.3f);
        here.Target = there;
        room.Portals.Add(here);
        var near = FacingWindow(world, texture, 128, 96, OneToOneDistance());
        var far = FacingWindow(world, texture, 128, 96, OneToOneDistance() * 5);
        far.Opacity = 0.7f;
        near.Frame = Framed(near, texture, WorldMaterial.Default with { SpecularStrength = 0.9f, Shininess = 48 });
        near.Room = far.Room = room;

        var target = host.DeviceAllocator!.Allocate(Width, Height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render)!;
        var settings = new WorldRenderSettings();
        var set = new WorldVisibleSet();
        var camera = Camera();
        void Frame(int i)
        {
            camera.Yaw = 0.02f * MathF.Sin(i * 0.1f);
            var view = camera.ViewOf(Full, Full);
            set.Clear();
            WorldVisibility.Collect(world, view, settings.RoomDepth, set);
            far.EditableLayers[0].ContentVersion = i / 50;
            near.Opacity = 0.6f + (0.4f * ((i % 40) / 40f));
            near.CanvasOpacity = (i % 40) / 40f;
            near.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.05f * MathF.Sin(i * 0.3f));
            renderer.Render(new WorldFrame(world, view, new FrameTick(i * 16_666_667L, 16_666_667), set, settings), target);
        }

        for (var i = 0; i < 60; i++)
        {
            Frame(i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Frame(i);
        }

        var measured = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, measured);
        if (Budgets.Recorded)
        {
            Budgets.Check("dungeon-wm", $"world-frame-{row}", measured);
        }

        ((BufferBase)target).Destroy();
        buffer.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_world_room(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var renderer = CreateRenderer(host, row);
        var rgba = RenderRoomScene(host, renderer);
        Golden.AssertMatches(rgba, Width, Height, $"world-room-{row}", tolerance: 2);
    }

    [Fact]
    public void The_vulkan_pass_agrees_with_the_gl_golden()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        using var host = new CompositorTestHost(renderer: "vulkan");
        using var renderer = CreateRenderer(host, "vulkan");
        var rgba = RenderRoomScene(host, renderer);
        var golden = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Goldens", "world-room-gl.png");
        var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(golden));
        Assert.Equal((Width, Height), (width, height));
        var differing = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var delta = Math.Max(Math.Abs(expected[i] - rgba[i]), Math.Max(Math.Abs(expected[i + 1] - rgba[i + 1]), Math.Abs(expected[i + 2] - rgba[i + 2])));
            if (delta > 2)
            {
                differing++;
            }
        }

        Assert.True(differing <= Width * Height / 2000, $"{differing} pixels differ from the gl golden by more than 2");
    }

    private static byte[] RenderRoomScene(CompositorTestHost host, IWorldRenderer renderer)
    {
        var checker = Checker(160, 120);
        var quadrants = Quadrants(128, 96);
        using var checkerTexture = host.Renderer.ImportTexture(checker)!;
        using var quadrantTexture = host.Renderer.ImportTexture(quadrants)!;
        var world = new World { ClearColor = new Vector3(0.05f, 0.05f, 0.08f), Ambient = new Vector3(0.2f) };
        var room = new WorldRoom(0, WorldRoomKind.Room, new WorldBounds(new Vector3(-2, -1, -4), new Vector3(2, 2, 1)));
        room.Structure = Room(4, 3, 5);
        world.Rooms.Add(room);
        room.Lights.Add(new WorldLight(new Vector3(-1.2f, 1.2f, -2.5f), new Vector3(1, 0.7f, 0.4f), 4, 1.4f, 0.15f, 0.5f));
        room.Lights.Add(new WorldLight(new Vector3(1.4f, 0.4f, -1f), new Vector3(0.4f, 0.6f, 1f), 3, 0.8f));

        var lit = FacingWindow(world, checkerTexture, 160, 120, 0);
        lit.Position = new Vector3(-0.6f, 0.3f, -3.4f);
        lit.Width = 1.2f;
        lit.Height = 0.9f;
        lit.Depth = 0.05f;
        var focused = FacingWindow(world, quadrantTexture, 128, 96, 0);
        focused.Position = new Vector3(1.0f, 0.1f, -2.6f);
        focused.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.6f);
        focused.Width = 0.8f;
        focused.Height = 0.6f;
        focused.Depth = 0.05f;
        focused.Focused = true;
        lit.Room = focused.Room = room;

        var portal = new WorldPortal(room, new Vector3(-1.6f, 0, -1.5f), MathF.PI / 2, 0.6f, 1.2f);
        var exit = new WorldPortal(room, new Vector3(0.6f, 0.3f, -3.3f), 0, 0.6f, 1.2f);
        portal.Target = exit;
        room.Portals.Add(portal);

        var camera = Camera();
        camera.Position = new Vector3(0, 0.4f, 0.5f);
        camera.Yaw = 0.1f;
        camera.Pitch = -0.05f;
        var rgba = Render(host, renderer, world, camera, Width, Height);
        checker.Destroy();
        quadrants.Destroy();
        return rgba;
    }

    internal static WorldMesh Room(float width, float height, float depth)
    {
        var vertices = new List<WorldVertex>();
        var indices = new List<uint>();
        void Add(Matrix4x4 transform)
        {
            var quad = WorldPrimitives.Quad;
            var first = (uint)vertices.Count;
            foreach (var vertex in quad.Vertices)
            {
                vertices.Add(new WorldVertex(
                    Vector3.Transform(vertex.Position, transform),
                    Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, transform)),
                    vertex.Uv));
            }

            foreach (var index in quad.Indices)
            {
                indices.Add(first + index);
            }
        }

        var floorY = -1f;
        var center = new Vector3(0, floorY + (height / 2), -depth / 2);
        Add(Matrix4x4.CreateScale(width, depth, 1) * Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(0, floorY, -depth / 2));
        Add(Matrix4x4.CreateScale(width, depth, 1) * Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, floorY + height, -depth / 2));
        Add(Matrix4x4.CreateScale(width, height, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, -depth));
        Add(Matrix4x4.CreateScale(depth, height, 1) * Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(-width / 2, center.Y, center.Z));
        Add(Matrix4x4.CreateScale(depth, height, 1) * Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(width / 2, center.Y, center.Z));
        return new WorldMesh([.. vertices], [.. indices], null);
    }

    internal static IWorldRenderer CreateRenderer(CompositorTestHost host, string row) => row switch
    {
        "gl" => new GlWorldRenderer(((GlRenderer)host.Renderer).Device),
        "vulkan" => new VulkanWorldRenderer(((VulkanRenderer)host.Renderer).Device),
        _ => throw new ArgumentException($"no world renderer for {row}"),
    };

    private static int Refreshed(IWorldRenderer renderer) => renderer switch
    {
        GlWorldRenderer gl => gl.CopiesRefreshed,
        VulkanWorldRenderer vulkan => vulkan.CopiesRefreshed,
        _ => -1,
    };

    private static int Direct(IWorldRenderer renderer) => renderer switch
    {
        GlWorldRenderer gl => gl.DirectSamples,
        VulkanWorldRenderer vulkan => vulkan.DirectSamples,
        _ => -1,
    };

    internal static WorldCamera Camera() => new()
    {
        Position = Vector3.Zero,
        FieldOfView = Fov,
        Near = 0.01f,
        Far = 100,
    };

    internal static float OneToOneDistance() => Height * 0.001f / (2 * MathF.Tan(Fov / 2));

    internal static WorldWindow FacingWindow(World world, ITexture texture, int width, int height, float distance)
    {
        var window = new WorldWindow
        {
            Position = new Vector3(0, 0, -distance),
            Width = width / 1000f,
            Height = height / 1000f,
            Depth = 0.004f,
            SurfaceWidth = width,
            SurfaceHeight = height,
        };
        window.AddLayer(new WorldSurfaceLayer { Texture = texture, Width = width, Height = height, Input = true });
        world.Windows.Add(window);
        return window;
    }

    internal static WorldWindowFrame Framed(WorldWindow window, ITexture? plaque, WorldMaterial material) => new()
    {
        Rail = 0.03f,
        Depth = 0.02f,
        Recess = 0.008f,
        Material = material,
        Moulding = WorldPrimitives.Moulding(window.Width, window.Height, 0.03f, 0.02f, 0.008f, new Vector4(0.5f, 0, 0, 1), null),
        PlaqueWidth = plaque is null ? 0 : window.Width,
        PlaqueHeight = plaque is null ? 0 : 0.02f,
        PlaquePixelWidth = 64,
        PlaquePixelHeight = 20,
        PlaqueTexture = plaque,
        PlaqueMaterial = WorldMaterial.Default with { Albedo = new Vector4(0.8f, 0.6f, 0.3f, 1), SpecularStrength = 0.8f, Shininess = 40 },
    };

    internal static byte[] Render(CompositorTestHost host, IWorldRenderer renderer, World world, WorldCamera camera, int width, int height, WorldRenderSettings? with = null)
    {
        var target = host.DeviceAllocator!.Allocate(width, height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render)!;
        var box = new Box(0, 0, width, height);
        var view = camera.ViewOf(box, box);
        var settings = with ?? new WorldRenderSettings();
        var set = new WorldVisibleSet();
        WorldVisibility.Collect(world, view, settings.RoomDepth, set);
        Assert.True(renderer.Render(new WorldFrame(world, view, new FrameTick(0, 16_666_667), set, settings), target));
        Assert.True(BufferCapture.TryReadRgba(target, host.Renderer, out var rgba));
        ((BufferBase)target).Destroy();
        return rgba;
    }

    internal static (int R, int G, int B) Pixel(byte[] rgba, int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    private static (int X, int Y) ProjectCenter(WorldWindow window)
    {
        Assert.True(Camera().ViewOf(Full, Full).TryProject(window.Position, out var x, out var y, out _));
        return ((int)x, (int)y);
    }

    private static bool DeepInside(in RenderTransform transform, double x, double y, int width, int height, double margin)
    {
        if (!TryInvert(transform, out var inverse))
        {
            return false;
        }

        var w = (inverse.M31 * x) + (inverse.M32 * y) + inverse.M33;
        var u = ((inverse.M11 * x) + (inverse.M12 * y) + inverse.M13) / w;
        var v = ((inverse.M21 * x) + (inverse.M22 * y) + inverse.M23) / w;
        return u > margin && v > margin && u < width - margin && v < height - margin;
    }

    private static bool TryInvert(in RenderTransform m, out RenderTransform inverse)
    {
        var det = (m.M11 * ((m.M22 * m.M33) - (m.M23 * m.M32))) -
            (m.M12 * ((m.M21 * m.M33) - (m.M23 * m.M31))) +
            (m.M13 * ((m.M21 * m.M32) - (m.M22 * m.M31)));
        if (Math.Abs(det) < 1e-12)
        {
            inverse = default;
            return false;
        }

        var s = 1 / det;
        inverse = new RenderTransform(
            ((m.M22 * m.M33) - (m.M23 * m.M32)) * s, ((m.M13 * m.M32) - (m.M12 * m.M33)) * s, ((m.M12 * m.M23) - (m.M13 * m.M22)) * s,
            ((m.M23 * m.M31) - (m.M21 * m.M33)) * s, ((m.M11 * m.M33) - (m.M13 * m.M31)) * s, ((m.M13 * m.M21) - (m.M11 * m.M23)) * s,
            ((m.M21 * m.M32) - (m.M22 * m.M31)) * s, ((m.M12 * m.M31) - (m.M11 * m.M32)) * s, ((m.M11 * m.M22) - (m.M12 * m.M21)) * s);
        return true;
    }

    internal static MemoryBuffer Solid(int width, int height, uint argb) => Filled(width, height, (_, _) => argb);

    internal static MemoryBuffer Quadrants(int width, int height) => Filled(width, height, (x, y) =>
        y < height / 2 ? (x < width / 2 ? 0xFFFF0000u : 0xFF00FF00u) : (x < width / 2 ? 0xFF0000FFu : 0xFFFFFFFFu));

    internal static MemoryBuffer Checker(int width, int height) => Filled(width, height, (x, y) =>
        ((x / 8) + (y / 8)) % 2 == 0 ? 0xFFE0C040u : (0xFF000000u | (uint)((x * 255 / width) << 16) | (uint)((y * 255 / height) << 8) | 0x40u));

    private static MemoryBuffer Filled(int width, int height, Func<int, int, uint> color)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < width; x++)
                {
                    row[x] = color(x, y);
                }
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }
}
