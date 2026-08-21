using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Basin.Scene;
using Basin.Capabilities;
using Basin.UI.Decoration;
using Basin.UI.Skia;
using SkiaSharp;
using Xunit;

namespace Basin.Tests;

public sealed class UIFrameTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    [Theory]
    [InlineData(1.0, "frame-appearance")]
    [InlineData(1.5, "frame-appearance-1.5x")]
    [InlineData(2.0, "frame-appearance-2x")]
    public void Frame_raster_matches_golden(double scale, string golden)
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux(),
            "these goldens carry shaped text, and glyph rasterization belongs to the host's Skia build");
        using var theme = new TestFrameTheme();
        var renderer = new TestFrameRenderer(theme);
        using var host = new SkiaUIHost();
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 96 + 8,
            Height = 64 + 34 + 4,
            Scale = scale,
        });
        Assert.NotNull(surface);

        var state = new FrameState { Title = "基盤 Basin", Active = true, Capabilities = FrameCapabilities.Maximize };
        renderer.Draw(surface, new Box(4, 34, 96, 64), state, default);
        Assert.True(surface.TryAcquire(out var frame));
        try
        {
            Golden.AssertMatches((MemoryBuffer)frame.Buffer!, golden);
        }
        finally
        {
            frame.Dispose();
            surface.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Decorated_scene_has_no_seam(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        foreach (var (scale, suffix) in new[] { (1.0, string.Empty), (1.5, "-1.5x"), (2.0, "-2x") })
        {
            using var host = new CompositorTestHost(width: 320, height: 240, renderer: renderer);
            using var state = new OutputState();
            Assert.True(host.Output.Commit(state.SetScale(scale)));

            using var theme = new TestFrameTheme();
            using var uiHost = new SkiaUIHost();
            var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);

            var geometry = new Box(20, 40, 64, 48);
            var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
            client.SetPosition(geometry.X, geometry.Y);

            frame.Configure(geometry, scale, new FrameState { Title = "基盤 Basin", Active = true });
            frame.Commit();
            host.RenderFrame();

            var insets = frame.Insets;
            frame.Dispose();
            client.Destroy();

            var outer = OutputScaling.ToPhysical(
                new Box(
                    geometry.X - insets.Left,
                    geometry.Y - insets.Top,
                    geometry.Width + insets.Left + insets.Right,
                    geometry.Height + insets.Top + insets.Bottom),
                scale).Intersect(new Box(0, 0, host.Target.Width, host.Target.Height));
            Assert.False(outer.IsEmpty);
            for (var y = outer.Y; y < outer.Bottom; y++)
            {
                for (var x = outer.X; x < outer.Right; x++)
                {
                    if (host.Pixel(x, y) == 0xFF000000u)
                    {
                        Assert.Fail($"background pixel at ({x},{y}) inside the decorated box at scale {scale} on {renderer}");
                    }
                }
            }

            Golden.AssertMatches(host, GoldenName($"frame-seam{suffix}", renderer));
        }
    }

    public static TheoryData<string> UIKinds => new() { "skia", "falsifier" };

    private static (IUIHost Host, IFrameRenderer Renderer, IDisposable? Theme) CreateUI(string kind)
    {
        if (kind == "falsifier")
        {
            return (new FalsifierUIHost(), new FalsifierFrameRenderer(), null);
        }

        var theme = new TestFrameTheme();
        return (new SkiaUIHost(), new TestFrameRenderer(theme), theme);
    }

    public static TheoryData<string> GpuRows => new() { "gl", "skia-gl", "vulkan", "skia-vulkan" };

    [Theory]
    [MemberData(nameof(GpuRows))]
    public void Gpu_hosted_frame_composites_without_seam(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(width: 320, height: 240, renderer: renderer);
        using var theme = new TestFrameTheme();
        using IUIHost uiHost = host.Renderer switch
        {
            Basin.Render.Skia.SkiaGlRenderer skiaGl =>
                new SkiaGlUIHost(skiaGl.Device, skiaGl.Device.CreateAllocator(), skiaGl.Context),
            Basin.Render.Gl.GlRenderer gl =>
                new SkiaGlUIHost(gl.Device, gl.Device.CreateAllocator()),
            Basin.Render.Skia.SkiaVulkanRenderer skiaVk =>
                new SkiaVulkanUIHost(skiaVk.Device, skiaVk.Context),
            Basin.Render.Vulkan.VulkanRenderer vulkan =>
                new SkiaVulkanUIHost(vulkan.Device),
            _ => throw new InvalidOperationException("not a GPU row"),
        };

        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);
        var geometry = new Box(20, 40, 64, 48);
        var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
        client.SetPosition(geometry.X, geometry.Y);

        frame.Configure(geometry, 1.0, new FrameState { Title = "基盤 Basin", Active = true });
        frame.Commit();
        host.RenderFrame();

        frame.PointerMotion(20 + 64 - 8, 40 - 15);
        host.RenderFrame();
        geometry = new Box(20, 40, 96, 60);
        client.Width = geometry.Width;
        client.Height = geometry.Height;
        frame.Configure(geometry, 1.0, new FrameState { Title = "基盤 Basin", Active = false });
        frame.Commit();
        host.RenderFrame();

        var insets = frame.Insets;
        var faulted = frame.IsFaulted;
        frame.Dispose();
        client.Destroy();

        Assert.False(faulted);
        var outer = new Box(
            geometry.X - insets.Left,
            geometry.Y - insets.Top,
            geometry.Width + insets.Left + insets.Right,
            geometry.Height + insets.Top + insets.Bottom);
        for (var y = outer.Y; y < outer.Bottom; y++)
        {
            for (var x = outer.X; x < outer.Right; x++)
            {
                if (host.Pixel(x, y) == 0xFF000000u)
                {
                    Assert.Fail($"background pixel at ({x},{y}) inside the GPU-decorated box on {renderer}");
                }
            }
        }
    }

    [Theory]
    [InlineData("pixman", "cpu")]
    [InlineData("gl", "cpu")]
    [InlineData("skia", "cpu")]
    [InlineData("vulkan", "cpu")]
    [InlineData("gl", "gpu")]
    [InlineData("skia-gl", "gpu")]
    [InlineData("vulkan", "gpu")]
    [InlineData("skia-vulkan", "gpu")]
    public void Decorated_damage_tracking_matches_full_repaint(string renderer, string hostKind)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        using var theme = new TestFrameTheme();
        using IUIHost uiHost = hostKind == "cpu" ? new SkiaUIHost() : host.Renderer switch
        {
            Basin.Render.Skia.SkiaGlRenderer skiaGl =>
                new SkiaGlUIHost(skiaGl.Device, skiaGl.Device.CreateAllocator(), skiaGl.Context),
            Basin.Render.Gl.GlRenderer gl =>
                new SkiaGlUIHost(gl.Device, gl.Device.CreateAllocator()),
            Basin.Render.Skia.SkiaVulkanRenderer skiaVk =>
                new SkiaVulkanUIHost(skiaVk.Device, skiaVk.Context),
            Basin.Render.Vulkan.VulkanRenderer vulkan =>
                new SkiaVulkanUIHost(vulkan.Device),
            _ => throw new InvalidOperationException("not a GPU row"),
        };
        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);
        var geometry = new Box(20, 40, 64, 48);
        var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
        client.SetPosition(geometry.X, geometry.Y);

        var failure = (string?)null;
        void Step(string what)
        {
            if (failure is not null)
            {
                return;
            }

            if (!sceneOutput.Commit(host.Renderer, swapchain, state, options))
            {
                failure = $"{what}: expected a commit";
                return;
            }

            host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
            failure = FirstPixelDifference(oracle, state.Buffer!, what);
        }

        frame.Configure(geometry, 1.0, new FrameState { Title = "基盤 Basin", Active = true });
        frame.Commit();
        Step("map");

        frame.PointerMotion(20 + 64 - 8, 40 - 15);
        Step("hover on");
        frame.PointerMotion(40, 40 - 15);
        Step("hover off");

        frame.Configure(geometry, 1.0, new FrameState { Title = "基盤 Basin", Active = false });
        frame.Commit();
        Step("focus flip");

        geometry = new Box(20, 40, 90, 60);
        client.Width = geometry.Width;
        client.Height = geometry.Height;
        frame.Configure(geometry, 1.0, new FrameState { Title = "基盤 Basin", Active = false });
        frame.Commit();
        Step("resize");

        frame.Visible = false;
        Step("hide");
        frame.Visible = true;
        Step("show");

        frame.Dispose();
        client.Destroy();
        oracle.Destroy();
        Assert.Null(failure);
    }

    private static unsafe string? FirstPixelDifference(IBuffer expected, IBuffer actual, string what)
    {
        if (!expected.BeginDataAccess(BufferDataAccess.Read, out var e))
        {
            return $"{what}: oracle unmappable";
        }

        if (!actual.BeginDataAccess(BufferDataAccess.Read, out var a))
        {
            expected.EndDataAccess();
            return $"{what}: presented buffer unmappable";
        }

        try
        {
            for (var y = 0; y < expected.Height; y++)
            {
                var er = new ReadOnlySpan<byte>((void*)(e.Data + y * e.Stride), expected.Width * 4);
                var ar = new ReadOnlySpan<byte>((void*)(a.Data + y * a.Stride), expected.Width * 4);
                if (!er.SequenceEqual(ar))
                {
                    return $"{what}: row {y} differs between damage-tracked and full repaint";
                }
            }
        }
        finally
        {
            actual.EndDataAccess();
            expected.EndDataAccess();
        }

        return null;
    }

    [Theory]
    [MemberData(nameof(UIKinds))]
    public void Configure_stages_without_showing_until_commit(string kind)
    {
        using var host = new CompositorTestHost();
        var (uiHost, renderer, theme) = CreateUI(kind);
        using var _ = theme;
        using var uiHostDisposer = uiHost;
        var frame = new Frame(uiHost, renderer, host.Scene.Root);

        var first = new Box(20, 40, 64, 48);
        frame.Configure(first, 1.0, new FrameState { Active = true });
        frame.Commit();
        host.RenderFrame();
        var titleBefore = host.Pixel(30, 40 - 20);

        var second = new Box(20, 40, 100, 70);
        frame.Configure(second, 1.0, new FrameState { Active = false });
        var staged = frame.HasPendingFor(second, 1.0);
        host.RenderFrame();
        var titleStaged = host.Pixel(30, 40 - 20);
        var borderStaged = host.Pixel(20 + 100 + 2, 40 + 10);

        frame.Commit();
        host.RenderFrame();
        var titleAfter = host.Pixel(30, 40 - 20);
        var borderAfter = host.Pixel(20 + 100 + 2, 40 + 10);

        frame.Dispose();

        Assert.NotEqual(0xFF000000u, titleBefore);
        Assert.True(staged);
        Assert.Equal(titleBefore, titleStaged);
        Assert.Equal(0xFF000000u, borderStaged);
        Assert.NotEqual(titleBefore, titleAfter);
        Assert.NotEqual(0xFF000000u, borderAfter);
    }

    [Theory]
    [MemberData(nameof(UIKinds))]
    public void PartAt_answers_from_the_shown_layout(string kind)
    {
        using var host = new CompositorTestHost();
        var (uiHost, renderer, theme) = CreateUI(kind);
        using var _ = theme;
        using var uiHostDisposer = uiHost;
        var frame = new Frame(uiHost, renderer, host.Scene.Root);

        var geometry = new Box(20, 40, 64, 48);
        Assert.Equal(FramePart.None, frame.PartAt(30, 15));

        frame.Configure(geometry, 1.0, new FrameState { Active = true, Capabilities = FrameCapabilities.Maximize });
        frame.Commit();

        Assert.Equal(FramePart.Title, frame.PartAt(45, 40 - 15));
        Assert.Equal(FramePart.Close, frame.PartAt(20 + 64 - 8, 40 - 15));
        Assert.Equal(FramePart.None, frame.PartAt(30, 50));
        Assert.Equal(FramePart.Left, frame.PartAt(20 - 2, 60));
        Assert.Equal(FramePart.BottomRight, frame.PartAt(20 + 64 + 2, 40 + 48 + 2));

        frame.Configure(new Box(20, 40, 100, 70), 1.0, new FrameState { Active = true });
        Assert.Equal(FramePart.None, frame.PartAt(20 + 80, 40 - 15));

        frame.Dispose();
    }

    [Fact]
    public void Faulting_renderer_tears_down_to_undecorated()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var frame = new Frame(uiHost, new ThrowingFrameRenderer(), host.Scene.Root);

        Exception? reported = null;
        frame.Faulted += e => reported = e;
        frame.Configure(new Box(20, 40, 64, 48), 1.0, default);

        Assert.True(frame.IsFaulted);
        Assert.NotNull(reported);
        frame.Commit();
        Assert.Equal(FramePart.None, frame.PartAt(30, 25));
        host.RenderFrame();
        Assert.Equal(0xFF000000u, host.Pixel(30, 25));

        frame.Dispose();
    }

    [Theory]
    [MemberData(nameof(UIKinds))]
    public void Frame_lifetime_leaves_counters_clean(string kind)
    {
        using var host = new CompositorTestHost();
        var (uiHost, renderer, theme) = CreateUI(kind);
        using (theme)
        using (uiHost)
        {
            var frame = new Frame(uiHost, renderer, host.Scene.Root);
            frame.Configure(new Box(20, 40, 64, 48), 1.0, new FrameState { Title = "基盤", Active = true });
            frame.Commit();
            host.RenderFrame();

            frame.Configure(new Box(20, 40, 80, 60), 1.0, new FrameState { Title = "基盤", Active = true });
            frame.Commit();
            host.RenderFrame();
            frame.PointerMotion(20 + 80 - 8, 40 - 15);
            host.RenderFrame();

            frame.Dispose();
        }

        host.RenderFrame();
    }

    [Fact]
    public void Hover_damages_button_rectangles_not_the_bar()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);
        var geometry = new Box(20, 40, 64, 48);
        frame.Configure(geometry, 1.0, new FrameState { Active = true });
        frame.Commit();
        host.RenderFrame();

        var damagedArea = 0L;
        host.Scene.Damaged += (_, box) => damagedArea += (long)box.Width * box.Height;

        for (var x = 30; x < 60; x += 3)
        {
            frame.PointerMotion(x, 40 - 15);
        }

        var titleSweepDamage = damagedArea;

        frame.PointerMotion(20 + 64 - 8, 40 - 15);
        frame.PointerMotion(40, 40 - 15);
        var buttonDamage = damagedArea - titleSweepDamage;

        frame.Dispose();

        Assert.Equal(0, titleSweepDamage);
        Assert.InRange(buttonDamage, 1, 2 * 16 * 16);
    }

    [Fact]
    public void Double_click_on_title_toggles_maximize()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);
        frame.Configure(new Box(20, 40, 64, 48), 1.0, default(FrameState));
        frame.Commit();

        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.PointerButton(40, 25, pressed: true, timeMs: 1000);
        frame.PointerButton(40, 25, pressed: false, timeMs: 1050);
        frame.PointerButton(40, 25, pressed: true, timeMs: 1200);
        Assert.Equal([FrameActionKind.Move, FrameActionKind.ToggleMaximize], actions);

        actions.Clear();
        frame.PointerButton(40, 25, pressed: true, timeMs: 5000);
        frame.PointerButton(40, 25, pressed: false, timeMs: 5050);
        frame.PointerButton(40, 25, pressed: true, timeMs: 6000);
        Assert.Equal([FrameActionKind.Move, FrameActionKind.Move], actions);

        frame.Dispose();
    }

    private const double TitleX = 40;
    private const double TitleY = 25;
    private const double CloseX = 72;
    private const double CloseY = 25;
    private const double RightEdgeX = 85;
    private const double RightEdgeY = 50;
    private const double OutsideRightX = 93;

    private static Frame TouchableFrame(CompositorTestHost host, SkiaUIHost uiHost, TestFrameTheme theme, double slop = 0)
    {
        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root) { TouchSlop = slop };
        frame.Configure(new Box(20, 40, 64, 48), 1.0, new FrameState { Capabilities = FrameCapabilities.Maximize });
        frame.Commit();
        return frame;
    }

    [Fact]
    public void A_touch_on_a_button_acts_on_the_lift()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(CloseX, CloseY, 0);
        Assert.Empty(actions);
        frame.TouchUp(CloseX, CloseY, 0);

        Assert.Equal([FrameActionKind.Close], actions);
        frame.Dispose();
    }

    [Fact]
    public void A_touch_slid_off_a_button_acts_on_nothing()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(CloseX, CloseY, 0);
        frame.TouchUp(TitleX, TitleY, 0);

        Assert.Empty(actions);
        frame.Dispose();
    }

    [Fact]
    public void A_touch_on_the_title_asks_for_a_move()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(TitleX, TitleY, 0);

        Assert.Equal([FrameActionKind.Move], actions);
        frame.Dispose();
    }

    [Fact]
    public void A_touch_on_an_edge_asks_for_a_resize()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameAction>();
        frame.Requested += actions.Add;

        frame.TouchDown(RightEdgeX, RightEdgeY, 0);

        Assert.Equal([new FrameAction(FrameActionKind.Resize, FrameEdges.Right)], actions);
        frame.Dispose();
    }

    [Fact]
    public void One_contact_owns_the_frame()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(CloseX, CloseY, 0);
        frame.TouchDown(TitleX, TitleY, 1);
        frame.TouchUp(TitleX, TitleY, 1);

        Assert.Empty(actions);
        frame.TouchUp(CloseX, CloseY, 0);
        Assert.Equal([FrameActionKind.Close], actions);
        frame.Dispose();
    }

    [Fact]
    public void A_latched_contact_locks_out_the_pointer()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(CloseX, CloseY, 0);
        frame.PointerButton(TitleX, TitleY, pressed: true, timeMs: 1000);

        Assert.Empty(actions);
        frame.Dispose();
    }

    [Fact]
    public void A_pointer_press_locks_out_a_contact()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.PointerButton(CloseX, CloseY, pressed: true, timeMs: 1000);
        frame.TouchDown(TitleX, TitleY, 0);

        Assert.Empty(actions);
        frame.Dispose();
    }

    [Fact]
    public void A_cancelled_contact_acts_on_nothing_and_frees_the_frame()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(CloseX, CloseY, 0);
        frame.TouchCancel();
        frame.TouchUp(CloseX, CloseY, 0);

        Assert.Empty(actions);
        frame.TouchDown(TitleX, TitleY, 1);
        Assert.Equal([FrameActionKind.Move], actions);
        frame.Dispose();
    }

    [Fact]
    public void Slop_reaches_an_edge_a_contact_missed()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme, slop: 12);
        var actions = new List<FrameAction>();
        frame.Requested += actions.Add;

        frame.TouchDown(OutsideRightX, RightEdgeY, 0);

        Assert.Equal([new FrameAction(FrameActionKind.Resize, FrameEdges.Right)], actions);
        frame.Dispose();
    }

    [Fact]
    public void Without_slop_the_same_contact_reaches_nothing()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameAction>();
        frame.Requested += actions.Add;

        frame.TouchDown(OutsideRightX, RightEdgeY, 0);

        Assert.Empty(actions);
        frame.Dispose();
    }

    [Fact]
    public void An_exact_hit_wins_over_the_slop()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme, slop: 12);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(TitleX, TitleY, 0);

        Assert.Equal([FrameActionKind.Move], actions);
        frame.Dispose();
    }

    [Fact]
    public void A_double_tap_on_the_title_toggles_maximize()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var frame = TouchableFrame(host, uiHost, theme);
        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.TouchDown(TitleX, TitleY, 0, timeMs: 1000);
        frame.TouchUp(TitleX, TitleY, 0);
        frame.TouchDown(TitleX, TitleY, 1, timeMs: 1200);
        Assert.Equal([FrameActionKind.Move, FrameActionKind.ToggleMaximize], actions);

        actions.Clear();
        frame.TouchDown(TitleX, TitleY, 2, timeMs: 5000);
        frame.TouchUp(TitleX, TitleY, 2);
        frame.TouchDown(TitleX, TitleY, 3, timeMs: 6000);
        Assert.Equal([FrameActionKind.Move, FrameActionKind.Move], actions);

        frame.Dispose();
    }

    [Fact]
    public void Menu_opens_inside_constraint_and_dismisses_by_destruction()
    {
        using var host = new CompositorTestHost(width: 320, height: 240);
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var overlay = new SceneTree(host.Scene.Root);
        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root)
        {
            MenuLayer = overlay,
            MenuConstraint = new Box(0, 0, 320, 240),
        };
        frame.Configure(new Box(180, 40, 120, 60), 1.0, default(FrameState));
        frame.Commit();
        host.RenderFrame();
        var beforeMenu = host.Pixel(200, 150);

        frame.MenuOrigin = default;
        frame.OpenMenu(290, 30);
        Assert.True(frame.IsMenuOpen);
        host.RenderFrame();
        var menuLeft = 320 - TestFrameRenderer.MenuWidth;
        Assert.NotEqual(0xFF000000u, host.Pixel(menuLeft + 4, 34));

        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);
        frame.MenuPointerMotion(10, TestFrameRenderer.MenuItemHeight + 5);
        frame.MenuPointerButton(10, TestFrameRenderer.MenuItemHeight + 5, pressed: false);
        Assert.Equal([FrameActionKind.Close], actions);
        Assert.False(frame.IsMenuOpen);
        host.RenderFrame();
        Assert.Equal(beforeMenu, host.Pixel(200, 150));

        frame.OpenMenu(200, 30);
        Assert.True(frame.IsMenuOpen);
        frame.Configure(new Box(180, 40, 120, 60), 1.0, new FrameState { Active = true });
        frame.Commit();
        Assert.False(frame.IsMenuOpen);

        frame.Dispose();
        overlay.Destroy();
    }

    [Fact]
    public void Menuless_renderer_surfaces_show_menu_to_the_consumer()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new FalsifierUIHost();
        var overlay = new SceneTree(host.Scene.Root);
        var frame = new Frame(uiHost, new FalsifierFrameRenderer(), host.Scene.Root)
        {
            MenuLayer = overlay,
        };
        frame.Configure(new Box(20, 40, 64, 48), 1.0, default(FrameState));
        frame.Commit();

        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);
        frame.OpenMenu(40, 25);
        Assert.False(frame.IsMenuOpen);
        Assert.Equal([FrameActionKind.ShowMenu], actions);

        frame.Dispose();
        overlay.Destroy();
    }

    [Theory]
    [InlineData("Basin.UI.Skia")]
    [InlineData("Basin.UI.Skia.Gpu")]
    public void UI_skia_never_references_the_system_font_manager(string assembly)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, $"{assembly}.dll"));
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeReferences)
        {
            var name = metadata.GetString(metadata.GetTypeReference(handle).Name);
            Assert.False(name == "SKFontManager", $"{assembly} references SKFontManager");
        }
    }

    private static string GoldenName(string name, string renderer) =>
        renderer == "pixman" ? name : $"{name}-{renderer}";
}

internal sealed class TestFrameTheme : IDisposable
{
    public TestFrameTheme()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotoSansCJK-Regular.ttc")!;
        using var data = SKData.Create(stream);
        Typeface = SkiaTypefaces.FromCollection(data, "Noto Sans CJK JP")
            ?? throw new InvalidOperationException("no JP face in the embedded collection");
        Font = Basin.Render.Skia.SkiaCensus.Track(new SKFont(Typeface, 14));
        Paint = Basin.Render.Skia.SkiaCensus.Track(new SKPaint { IsAntialias = true });
        Text = new SkiaShapedTextCache(Typeface);
    }

    public SKTypeface Typeface { get; }

    public SKFont Font { get; }

    public SKPaint Paint { get; }

    public SkiaShapedTextCache Text { get; }

    public void Dispose()
    {
        Text.Dispose();
        Basin.Render.Skia.SkiaCensus.Release(Paint);
        Basin.Render.Skia.SkiaCensus.Release(Font);
        Basin.Render.Skia.SkiaCensus.Release(Typeface);
    }
}

internal sealed class TestFrameRenderer(TestFrameTheme theme) : IFrameRenderer
{
    private const int Border = 4;
    private const int TitleHeight = 30;
    private const int CloseSide = 16;

    private int _outerWidth;
    private int _outerHeight;

    public FrameInsets Measure(in FrameState state, double scale) =>
        new(Border + TitleHeight, Border, Border, Border);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        _outerWidth = clientBox.Width + 2 * Border;
        _outerHeight = clientBox.Height + TitleHeight + 2 * Border;
        var canvas = skia.BeginDraw();
        try
        {
            var chrome = state.Active ? new SKColor(0x28, 0x46, 0x78) : new SKColor(0x40, 0x40, 0x44);
            var barBottom = Border + TitleHeight;
            theme.Paint.Color = chrome;
            canvas.DrawRect(0, 0, _outerWidth, barBottom, theme.Paint);
            canvas.DrawRect(0, barBottom, Border, clientBox.Height, theme.Paint);
            canvas.DrawRect(_outerWidth - Border, barBottom, Border, clientBox.Height, theme.Paint);
            canvas.DrawRect(0, _outerHeight - Border, _outerWidth, Border, theme.Paint);

            theme.Paint.Color = interaction.Hot == FramePart.Close
                ? new SKColor(0xD0, 0x50, 0x50)
                : new SKColor(0xB0, 0x30, 0x30);
            var closeBox = CloseBox();
            canvas.DrawRect(closeBox.X, closeBox.Y, closeBox.Width, closeBox.Height, theme.Paint);

            if (state.Title is { Length: > 0 } title && theme.Text.TryGetBlob(title, theme.Font, out var blob, out _))
            {
                var metrics = theme.Font.Metrics;
                theme.Paint.Color = SKColors.White;
                canvas.DrawText(blob, Border + 6, Border + (TitleHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent, theme.Paint);
            }
        }
        finally
        {
            skia.EndDraw();
        }
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale)
    {
        var w = _outerWidth;
        var h = _outerHeight;
        if (w <= 0 || x < 0 || y < 0 || x >= w || y >= h)
        {
            return FramePart.None;
        }

        if (x >= w - Border && y >= h - Border)
        {
            return FramePart.BottomRight;
        }

        if (x < Border)
        {
            return FramePart.Left;
        }

        if (x >= w - Border)
        {
            return FramePart.Right;
        }

        if (y >= h - Border)
        {
            return FramePart.Bottom;
        }

        if (y < Border + TitleHeight)
        {
            var close = CloseBox();
            if (x >= close.X && x < close.Right && y >= close.Y && y < close.Bottom)
            {
                return FramePart.Close;
            }

            return y < Border ? FramePart.Top : FramePart.Title;
        }

        return FramePart.Border;
    }

    public Box PartBounds(FramePart part) => part == FramePart.Close ? CloseBox() : default;

    public const int MenuWidth = 120;
    public const int MenuItemHeight = 24;

    public UISurfaceSize MeasureMenu(in FrameState state, double scale) => new(MenuWidth, 2 * MenuItemHeight, scale);

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        var skia = (ISkiaUISurface)surface;
        var canvas = skia.BeginDraw();
        try
        {
            theme.Paint.Color = new SKColor(0x20, 0x20, 0x28);
            canvas.DrawRect(0, 0, MenuWidth, 2 * MenuItemHeight, theme.Paint);
            if (hotItem is 0 or 1)
            {
                theme.Paint.Color = new SKColor(0x50, 0x50, 0x60);
                canvas.DrawRect(0, hotItem * MenuItemHeight, MenuWidth, MenuItemHeight, theme.Paint);
            }
        }
        finally
        {
            skia.EndDraw();
        }
    }

    public int MenuItemAt(double x, double y, in FrameState state, double scale) =>
        x < 0 || x >= MenuWidth || y < 0 ? -1 : (int)(y / MenuItemHeight) switch { 0 => 0, 1 => 1, _ => -1 };

    public FrameAction? MenuItemAction(int item, in FrameState state) =>
        item == 0 ? new FrameAction(FrameActionKind.ToggleMaximize) : new FrameAction(FrameActionKind.Close);

    private Box CloseBox() =>
        new(_outerWidth - Border - 4 - CloseSide, Border + (TitleHeight - CloseSide) / 2, CloseSide, CloseSide);
}

internal sealed class ThrowingFrameRenderer : IFrameRenderer
{
    public FrameInsets Measure(in FrameState state, double scale) => new(34, 4, 4, 4);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction) =>
        throw new InvalidOperationException("deliberate renderer failure");

    public FramePart PartAt(double x, double y, in FrameState state, double scale) => FramePart.Title;
}
