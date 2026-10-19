using Basin.Capabilities;
using Basin.Effects;
using Basin.Frames.Quill;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class GlassFrameGoldenTests
{
    private const int Width = 320;
    private const int Height = 240;
    private static readonly Box Client = new(60, 70, 200, 120);

    public static TheoryData<string, GlassPass, bool> Rows => new()
    {
        { "gl", GlassPass.Composite, true },
        { "gl", GlassPass.Composite, false },
        { "gl", GlassPass.Fused, true },
        { "gl", GlassPass.Fused, false },
        { "vulkan", GlassPass.Composite, true },
        { "vulkan", GlassPass.Composite, false },
        { "vulkan", GlassPass.Fused, true },
        { "vulkan", GlassPass.Fused, false },
    };

    public static TheoryData<string> LeakRows => ["gl", "vulkan", "skia-gl", "skia-vulkan"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void Golden_glass_frame_over_a_known_backdrop(string row, GlassPass pass, bool active)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var host = new CompositorTestHost(width: Width, height: Height, renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, row);
        Assert.SkipWhen(lease is null, $"this row builds no {row} quill host");
        using var glass = BackdropGlass.For(host.Renderer)!;
        glass.Options = new GlassOptions { Pass = pass };

        using var scene = new GlassScene(host, lease!.Host, glass);
        scene.Frame.Configure(Client, 1.0, State(active));
        scene.Frame.Commit();
        host.RenderFrame();

        var name = $"glass-frame-{(pass == GlassPass.Fused ? "fused" : "composite")}-{(active ? "active" : "inactive")}-{row}";
        Golden.AssertMatches(host, name, gpuTolerance: 3);
    }

    [Theory]
    [MemberData(nameof(LeakRows))]
    public void A_glass_frame_maps_animates_flips_focus_and_closes_clean(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(width: Width, height: Height, renderer: row);
        var backend = row.Contains("vulkan", StringComparison.Ordinal) ? "vulkan" : "gl";
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");
        using var glass = BackdropGlass.For(host.Renderer)!;
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;

        using (var scene = new GlassScene(host, lease!.Host, glass))
        {
            var frame = scene.Frame;
            frame.Clock = clock;
            frame.Time = () => now;
            frame.Configure(Client, 1.0, State(active: true));
            frame.Commit();
            host.RenderFrame();
            Assert.False(frame.Animating);

            frame.PointerEnter(Client.X + 40, Client.Y - 20);
            Assert.True(frame.Animating);
            for (var i = 0; i < 30 && frame.Animating; i++)
            {
                now += 16_000_000;
                clock.BeginFrame(host.Output, now);
                host.RenderFrame();
            }

            Assert.False(frame.Animating);
            frame.PointerMotion(Client.X + 120, Client.Y - 20);
            Assert.True(frame.Animating);

            frame.Configure(Client, 1.0, State(active: false));
            frame.Commit();
            for (var i = 0; i < 30 && frame.Animating; i++)
            {
                now += 16_000_000;
                clock.BeginFrame(host.Output, now);
                host.RenderFrame();
            }

            Assert.False(frame.Animating);
            frame.PointerLeave();
            for (var i = 0; i < 30 && frame.Animating; i++)
            {
                now += 16_000_000;
                clock.BeginFrame(host.Output, now);
                host.RenderFrame();
            }

            Assert.False(frame.Animating);
        }

        host.RenderFrame();
    }

    private static FrameState State(bool active) => new()
    {
        Title = "Glass",
        AppId = "basin.glass",
        Active = active,
        Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize,
    };

    private sealed class GlassScene : IDisposable
    {
        private readonly MemoryBuffer _floor = new(Width, Height, DrmFormat.Xrgb8888);
        private readonly MemoryBuffer _body = new(Client.Width, Client.Height, DrmFormat.Argb8888);
        private readonly SceneBuffer _floorNode;
        private readonly SceneBuffer _bodyNode;

        public GlassScene(CompositorTestHost host, IUIHost ui, IBackdropGlass glass)
        {
            Stripes(_floor);
            Translucent(_body);
            _floorNode = new SceneBuffer(host.Scene.Root);
            _floorNode.SetBuffer(_floor);
            var theme = QuillFrameTheme.DarkGlass();
            theme.Face = QuillFrameFonts.Bundled();
            Frame = new Frame(ui, new QuillFrameRenderer(theme), host.Scene.Root)
            {
                UnderlayEffect = glass,
                Time = static () => 1_000_000_000L,
            };
            _bodyNode = new SceneBuffer(host.Scene.Root);
            _bodyNode.SetBuffer(_body);
            _bodyNode.SetPosition(Client.X, Client.Y);
        }

        public Frame Frame { get; }

        public void Dispose()
        {
            Frame.Dispose();
            _bodyNode.Destroy();
            _floorNode.Destroy();
            _body.Destroy();
            _floor.Destroy();
        }

        private static unsafe void Stripes(MemoryBuffer buffer)
        {
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
            for (var y = 0; y < buffer.Height; y++)
            {
                var line = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < buffer.Width; x++)
                {
                    line[x] = ((x + y) / 12 % 3) switch { 0 => 0xFFE04030u, 1 => 0xFF30C060u, _ => 0xFF3050E0u };
                }
            }

            buffer.EndDataAccess();
        }

        private static unsafe void Translucent(MemoryBuffer buffer)
        {
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
            for (var y = 0; y < buffer.Height; y++)
            {
                var line = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < buffer.Width; x++)
                {
                    line[x] = x < 40 && y < 20 ? 0xFFF0F0F0u : 0x30101010u;
                }
            }

            buffer.EndDataAccess();
        }
    }
}
