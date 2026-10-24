using System.Numerics;
using Basin;
using Basin.Diagnostics;
using Basin.Physics;
using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Render3D.Gl;
using Basin.Render3D.Vulkan;
using Basin.Scene3D;
using Xunit;

namespace DungeonWm.Tests;

public sealed class GoldenTests
{
    private const int Width = 480;
    private const int Height = 270;
    private static readonly Lazy<Kit> SharedKit = new(Kit.Load);

    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_kit_room_with_three_windows_under_stone(string row)
    {
        using var stage = Stage.Create(row);
        var world = World(new DungeonWmSettings { Rooms = 1 });
        var room = world.Map.Rooms[0];
        var slots = world.SlotsOf(room).Where(s => s.Side == GridSide.North).ToList();
        for (var i = 0; i < Math.Min(3, slots.Count); i++)
        {
            stage.Hang(world, slots[i], 320 - (i * 60), 240 - (i * 40), (uint)(0xFF3070C0 + (i * 0x305020)));
        }

        var north = world.CenterOf(room, 1.6f);
        north.Z = (slots[0].Center(world.Unit).Z) + 5.5f;
        var camera = CameraAt(north, 0, 0.05f);
        stage.Check(world, camera, $"dungeon-wm-room-stone-{row}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_three_framed_windows_one_per_style(string row)
    {
        using var stage = Stage.Create(row);
        var world = World(new DungeonWmSettings { Rooms = 1 });
        var frames = new DungeonWmFrames(SharedKit.Value.Images[0], new FrameSettings(), 7);
        var room = world.Map.Rooms[0];
        var slots = world.SlotsOf(room).Where(s => s.Side == GridSide.North).ToList();
        string[] styles = ["oak", "gilt", "iron"];
        for (var i = 0; i < Math.Min(3, slots.Count); i++)
        {
            var window = stage.Hang(world, slots[i], 320 - (i * 60), 240 - (i * 40), (uint)(0xFF3070C0 + (i * 0x305020)));
            frames.Apply(window, frames.Style(styles[i]), shown: true);
        }

        var north = world.CenterOf(room, 1.6f);
        north.Z = slots[0].Center(world.Unit).Z + 5.5f;
        stage.Check(world, CameraAt(north, 0, 0.05f), $"dungeon-wm-framed-styles-{row}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_a_gilt_frame_glints_under_a_torch(string row)
    {
        using var stage = Stage.Create(row);
        var world = World(new DungeonWmSettings { Rooms = 1 });
        var frames = new DungeonWmFrames(SharedKit.Value.Images[0], new FrameSettings(), 7);
        var room = world.Map.Rooms[0];
        var slot = world.SlotsOf(room).First(s => s.Side == GridSide.North);
        var window = stage.Hang(world, slot, 300, 220, 0xFF804030);
        frames.Apply(window, frames.Style("gilt"), shown: true);
        world.RoomOf(room).Lights.Add(new WorldLight(
            window.Position + new Vector3(0.75f, 0.35f, 0.25f), new Vector3(1, 0.62f, 0.3f), 2.5f, 1.6f));
        var eye = window.Position + (window.Normal * 1.6f) + new Vector3(-0.5f, 0.1f, 0);
        var camera = CameraAt(eye, -0.3f, -0.05f);
        stage.Check(world, camera, $"dungeon-wm-gilt-torch-{row}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_kit_room_under_wood_with_beams(string row)
    {
        using var stage = Stage.Create(row);
        var settings = new DungeonWmSettings { Rooms = 1 };
        settings.Bake = settings.Bake with { Roof = RoofKind.Wood, RoofBeams = true };
        var world = World(settings);
        var room = world.Map.Rooms[0];
        var camera = CameraAt(world.CenterOf(room, 1.6f) + new Vector3(0, 0, 2), 0, 0.35f);
        stage.Check(world, camera, $"dungeon-wm-room-wood-{row}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_corridor_through_a_doorway(string row)
    {
        using var stage = Stage.Create(row);
        var world = World(new DungeonWmSettings { Rooms = 2 });
        var doorway = world.Map.Doorways[0];
        var inside = doorway.Room.Cells.Contains(doorway.Edge.Cell) ? doorway.Edge.Cell : doorway.Edge.Mirror.Cell;
        var toward = inside == doorway.Edge.Cell ? doorway.Edge.Side : GridSides.Opposite(doorway.Edge.Side);
        var eye = (GridSides.CellCenter(inside) - (GridSides.Inward(toward) * -0)) * world.Unit;
        eye.Y = 1.6f;
        var direction = -GridSides.Inward(toward);
        var camera = CameraAt(eye - (direction * 1.5f), MathF.Atan2(-direction.X, -direction.Z), 0);
        stage.Check(world, camera, $"dungeon-wm-corridor-{row}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_a_thrown_window_at_rest_among_barrels(string row)
    {
        using var stage = Stage.Create(row);
        var settings = new DungeonWmSettings { Rooms = 1 };
        var world = World(settings);
        var room = world.Map.Rooms[0];
        var center = world.CenterOf(room, 0);
        var slab = stage.Window(world, 150, 100, 0xFF20A060);
        slab.Position = center + new Vector3(0, 1.6f, -2.5f);
        slab.Room = world.RoomOf(room);
        using var physics = new PhysicsWorld3D(Bepu3DEngine.Instance, new Vector3(0, -9.81f, 0));
        foreach (var box in world.RoomOf(room).Blockers)
        {
            physics.AddStaticBox(box.Center, box.Size * 0.5f);
        }

        var barrels = new List<(WorldProp Prop, PhysicsBody3D Body)>();
        var barrelMesh = SharedKit.Value["barrel_small"].Mesh;
        for (var i = 0; i < 3; i++)
        {
            var prop = new WorldProp(barrelMesh) { Room = world.RoomOf(room), Position = center + new Vector3(-0.6f + (i * 0.6f), 0, -4.2f) };
            world.World.Props.Add(prop);
            var body = physics.AddCylinder(0.37f, 0.38f, PhysicsPose3D.At(prop.Position + new Vector3(0, 0.38f, 0)), new PhysicsMaterial(Density: 60, Friction: 0.6, Restitution: 0.15, GravityScale: 1, LinearDamping: 0.2));
            barrels.Add((prop, body));
        }

        var window = physics.AddBox(new Vector3(0.3f, 0.2f, 0.01f), new PhysicsPose3D(slab.Position, Quaternion.Identity), new PhysicsMaterial(Density: 400, Friction: 0.5, Restitution: 0.25, GravityScale: 1, LinearDamping: 0.15));
        physics.SetVelocity(window, new Vector3(0.3f, 1.5f, -2.0f), new Vector3(-1.2f, 0.8f, 0.2f));
        for (var i = 0; i < 240; i++)
        {
            physics.StepOnce();
        }

        var pose = window.Current;
        slab.Orientation = pose.Orientation;
        slab.Position = pose.Position + Vector3.Transform(new Vector3(0, 0, 0.01f), pose.Orientation);
        foreach (var (prop, body) in barrels)
        {
            var at = body.Current;
            prop.Orientation = at.Orientation;
            prop.Position = at.Position - Vector3.Transform(new Vector3(0, 0.38f, 0), at.Orientation);
        }

        var camera = CameraAt(center + new Vector3(0, 1.6f, -1f), 0, -0.45f);
        stage.Check(world, camera, $"dungeon-wm-thrown-{row}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_a_closed_window_falls_off_its_wall(string row)
    {
        using var stage = Stage.Create(row);
        var world = World(new DungeonWmSettings { Rooms = 1 });
        var frames = new DungeonWmFrames(SharedKit.Value.Images[0], new FrameSettings(), 7);
        var room = world.Map.Rooms[0];
        var slots = world.SlotsOf(room).Where(s => s.Side == GridSide.North).ToList();
        var hung = stage.Hang(world, slots[0], 300, 220, 0xFF3070C0);
        frames.Apply(hung, frames.Style("oak"), shown: true);
        hung.CanvasOpacity = 0.5f;
        hung.Opacity = 0.8f;

        var falling = stage.Hang(world, slots[Math.Min(1, slots.Count - 1)], 280, 200, 0xFFC05030);
        frames.Apply(falling, frames.Style("iron"), shown: true);
        falling.Pickable = false;
        var wall = falling.Orientation;
        falling.Orientation = wall * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.55f) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.12f);
        falling.Position += (falling.Normal * 0.35f) - new Vector3(0, 0.6f, 0);

        var north = world.CenterOf(room, 1.6f);
        north.Z = slots[0].Center(world.Unit).Z + 4.5f;
        stage.Check(world, CameraAt(north, 0, -0.1f), $"dungeon-wm-falling-{row}");
    }

    private static DungeonWmWorld World(DungeonWmSettings settings)
    {
        var world = new DungeonWmWorld(SharedKit.Value, settings);
        for (var i = 0; i < settings.Rooms; i++)
        {
            world.AddRoom();
        }

        return world;
    }

    private static WorldCamera CameraAt(Vector3 eye, float yaw, float pitch) => new()
    {
        Position = eye,
        Yaw = yaw,
        Pitch = pitch,
        FieldOfView = MathF.PI * 70 / 180,
        Near = 0.05f,
        Far = 120,
    };

    private sealed class Stage : IDisposable
    {
        private readonly RenderStack _stack;
        private readonly List<ITexture> _textures = [];
        private readonly List<MemoryBuffer> _buffers = [];

        private Stage(RenderStack stack, IWorldRenderer renderer)
        {
            _stack = stack;
            Renderer = renderer;
        }

        public IWorldRenderer Renderer { get; }

        public static Stage Create(string row)
        {
            Assert.SkipUnless(File.Exists("/dev/dri/renderD128"), "no render node");
            var node = Basin.Renderers.RendererCatalog.FindRenderNode();
            Assert.SkipWhen(node is null, "no render node");
            RenderStack stack;
            try
            {
                stack = Basin.Renderers.RendererCatalog.Create(row, node!);
            }
            catch (Exception error) when (error is InvalidOperationException or DllNotFoundException)
            {
                Assert.Skip($"{row} is not available: {error.Message}");
                throw;
            }

            IWorldRenderer renderer = row == "gl"
                ? new GlWorldRenderer(((GlRenderer)stack.Renderer).Device)
                : new VulkanWorldRenderer(((VulkanRenderer)stack.Renderer).Device);
            return new Stage(stack, renderer);
        }

        public WorldWindow Window(DungeonWmWorld world, int width, int height, uint argb)
        {
            var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < height; y++)
                {
                    var line = (uint*)(view.Data + (y * view.Stride));
                    for (var x = 0; x < width; x++)
                    {
                        line[x] = (x / 16 + y / 16) % 2 == 0 ? argb : 0xFFF0F0E8;
                    }
                }
            }

            buffer.EndDataAccess();
            _buffers.Add(buffer);
            var texture = _stack.Renderer.ImportTexture(buffer)!;
            _textures.Add(texture);
            var window = new WorldWindow
            {
                Width = width / 250f,
                Height = height / 250f,
                SurfaceWidth = width,
                SurfaceHeight = height,
            };
            window.AddLayer(new WorldSurfaceLayer { Texture = texture, Width = width, Height = height, Input = true });
            world.World.Windows.Add(window);
            return window;
        }

        public WorldWindow Hang(DungeonWmWorld world, WallSlot slot, int width, int height, uint argb)
        {
            var window = Window(world, width, height, argb);
            var position = slot.Center(world.Unit) + (slot.Inward * ((0.5f * world.Unit) + 0.04f));
            position.Y = 1.6f;
            window.Position = position;
            window.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, slot.Yaw);
            window.Room = world.RoomOf(slot.Room);
            return window;
        }

        public void Check(DungeonWmWorld world, WorldCamera camera, string name, WorldRenderSettings? settings = null)
        {
            settings ??= new WorldRenderSettings();
            var target = _stack.DeviceAllocator!.Allocate(Width, Height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render)!;
            var box = new Box(0, 0, Width, Height);
            var view = camera.ViewOf(box, box);
            var set = new WorldVisibleSet();
            WorldVisibility.Collect(world.World, view, settings.RoomDepth, set);
            Assert.True(Renderer.Render(new WorldFrame(world.World, view, new FrameTick(0, 16_666_667), set, settings), target));
            Assert.True(BufferCapture.TryReadRgba(target, _stack.Renderer, out var rgba));
            ((BufferBase)target).Destroy();
            Compare(rgba, name);
        }

        public long Measure(DungeonWmWorld world, WorldCamera camera, WorldRenderSettings settings, int frames, Action step)
        {
            var target = _stack.DeviceAllocator!.Allocate(Width, Height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render)!;
            var box = new Box(0, 0, Width, Height);
            var view = camera.ViewOf(box, box);
            var set = new WorldVisibleSet();
            long before = 0;
            for (var i = 0; i < frames; i++)
            {
                if (i == frames / 2)
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                }

                step();
                WorldVisibility.Collect(world.World, view, settings.RoomDepth, set);
                Renderer.Render(new WorldFrame(world.World, view, new FrameTick(i * 16_666_667L, 16_666_667), set, settings), target);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            ((BufferBase)target).Destroy();
            return allocated;
        }

        public void Dispose()
        {
            foreach (var texture in _textures)
            {
                texture.Dispose();
            }

            Renderer.Dispose();
            foreach (var buffer in _buffers)
            {
                buffer.Destroy();
            }

            _stack.DeviceAllocator?.Dispose();
            _stack.Renderer.Dispose();
        }

        private static void Compare(byte[] rgba, string name)
        {
            var path = Path.Combine(ProcessTests.Repository(), "tests", "DungeonWm.Tests", "Goldens", $"{name}.png");
            if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, PngCodec.Encode(rgba, Width, Height));
                return;
            }

            Assert.True(File.Exists(path), $"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {path}.");
            var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(path));
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

            if (differing > 0)
            {
                var actual = Path.Combine(Path.GetTempPath(), $"dungeon-wm-golden-{name}-actual.png");
                File.WriteAllBytes(actual, PngCodec.Encode(rgba, Width, Height));
                Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond 2. Actual written to {actual}.");
            }
        }
    }
}
