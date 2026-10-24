using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Basin.Scene;
using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;
using Xunit;

namespace Basin.Tests;

public sealed class UIFrameTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

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

    [Theory]
    [InlineData(FramePart.Shade, FrameActionKind.ToggleShade)]
    [InlineData(FramePart.Above, FrameActionKind.ToggleAbove)]
    [InlineData(FramePart.Stick, FrameActionKind.ToggleSticky)]
    public void A_release_over_a_state_button_asks_for_its_toggle(FramePart part, FrameActionKind expected)
    {
        using var host = new CompositorTestHost();
        using var uiHost = new FalsifierUIHost();
        var frame = new Frame(uiHost, new OnePartFrameRenderer(part), host.Scene.Root);
        frame.Configure(new Box(20, 40, 64, 48), 1.0, default(FrameState));
        frame.Commit();

        var actions = new List<FrameActionKind>();
        frame.Requested += a => actions.Add(a.Kind);

        frame.PointerButton(TitleX, TitleY, pressed: true);
        Assert.Empty(actions);
        frame.PointerButton(TitleX, TitleY, pressed: false);
        Assert.Equal([expected], actions);

        actions.Clear();
        frame.TouchDown(TitleX, TitleY, 0);
        frame.TouchUp(TitleX, TitleY, 0);
        Assert.Equal([expected], actions);

        frame.Dispose();
    }

    [Fact]
    public void A_bounded_title_is_shaped_to_the_width_with_an_ellipsis()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "shaping belongs to the host's Skia build");
        using var theme = new TestFrameTheme();
        const string title = "A title that is far too long for the room it is given";
        Assert.True(theme.Text.TryGetBlob(title, theme.Font, out _, out var full));
        Assert.True(theme.Text.TryGetBlob(title, theme.Font, full / 2, out var bounded, out var width));
        Assert.True(width <= full / 2);
        Assert.True(width > 0);
        Assert.True(theme.Text.TryGetBlob(title, theme.Font, full / 2, out var again, out var widthAgain));
        Assert.Same(bounded, again);
        Assert.Equal(width, widthAgain);
        Assert.True(theme.Text.TryGetBlob(title, theme.Font, full * 2, out var whole, out var wholeWidth));
        Assert.Equal(full, wholeWidth);
        Assert.NotSame(bounded, whole);
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

    [Fact]
    public void Outsets_grow_the_surface_and_let_input_through()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var beneath = new SceneRect(host.Scene.Root, 300, 200, new RenderColor(0.2f, 0.2f, 0.2f, 1f));
        var renderer = new OutsetFrameRenderer();
        var frame = new Frame(uiHost, renderer, host.Scene.Root);
        var geometry = new Box(40, 60, 64, 48);
        frame.Configure(geometry, 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();

        Assert.Equal(new FrameInsets(6, 6, 6, 6), frame.Outsets);
        Assert.Equal(new Box(36, 40, 72, 72), frame.OuterBounds);
        Assert.Equal(new Box(30, 34, 84, 84), frame.DrawnBounds);
        Assert.Equal(new Box(10, 26, 64, 48), renderer.LastClient);
        Assert.Equal((84, 84), renderer.LastSurface);

        Assert.Same(beneath, host.Scene.NodeAt(32, 80)!.Value.Node);
        Assert.Same(beneath, host.Scene.NodeAt(70, 36)!.Value.Node);
        Assert.Equal(FramePart.None, frame.PartAt(32, 80));
        Assert.True(frame.OwnsNode(host.Scene.NodeAt(37, 80)!.Value.Node));
        Assert.Equal(FramePart.Border, frame.PartAt(37, 80));
        Assert.True(frame.OwnsNode(host.Scene.NodeAt(70, 45)!.Value.Node));
        Assert.Equal(FramePart.Title, frame.PartAt(70, 45));

        frame.Dispose();
        beneath.Destroy();
    }

    [Fact]
    public void A_none_part_inside_the_frame_lets_input_through()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var beneath = new SceneRect(host.Scene.Root, 300, 200, new RenderColor(0.2f, 0.2f, 0.2f, 1f));
        var frame = new Frame(uiHost, new OutsetFrameRenderer(), host.Scene.Root);
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();

        Assert.Equal(FramePart.None, frame.PartAt(36, 40));
        Assert.Same(beneath, host.Scene.NodeAt(36, 40)!.Value.Node);
        Assert.True(frame.OwnsNode(host.Scene.NodeAt(37, 40)!.Value.Node));

        frame.Dispose();
        beneath.Destroy();
    }

    [Fact]
    public void An_opaque_frame_still_takes_every_frame_pixel()
    {
        using var host = new CompositorTestHost();
        using var theme = new TestFrameTheme();
        using var uiHost = new SkiaUIHost();
        var beneath = new SceneRect(host.Scene.Root, 300, 200, new RenderColor(0.2f, 0.2f, 0.2f, 1f));
        var frame = new Frame(uiHost, new TestFrameRenderer(theme), host.Scene.Root);
        frame.Configure(new Box(20, 40, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();

        foreach (var (x, y) in new[] { (16.0, 6.0), (16.0, 60.0), (50.0, 90.0), (87.0, 60.0), (50.0, 20.0) })
        {
            Assert.True(frame.OwnsNode(host.Scene.NodeAt(x, y)!.Value.Node), $"({x},{y})");
        }

        frame.Dispose();
        beneath.Destroy();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_placement_change_redraws_only_a_renderer_that_follows_it(bool follows)
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var renderer = new OutsetFrameRenderer { Follows = follows };
        var frame = new Frame(uiHost, renderer, host.Scene.Root);
        var first = new FramePlacement(100, 80, 1920, 1080);
        frame.Placement = first;
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();
        Assert.Equal(first, renderer.LastState.Placement);
        var configured = renderer.Draws;

        var moved = first with { X = 300 };
        frame.Placement = moved;
        frame.Placement = moved;

        Assert.Equal(follows ? configured + 1 : configured, renderer.Draws);
        Assert.Equal(follows ? moved : first, renderer.LastState.Placement);

        frame.Configure(new Box(40, 60, 64, 52), 1.0, new FrameState { Title = "t", Active = true });
        Assert.Equal(moved, renderer.LastState.Placement);
        frame.Dispose();
    }

    [Fact]
    public void Animating_repaints_on_the_frame_clock_and_stops()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var renderer = new OutsetFrameRenderer();
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;
        var frame = new Frame(uiHost, renderer, host.Scene.Root) { Clock = clock, Time = () => now };
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();
        Assert.False(frame.Animating);

        clock.BeginFrame(host.Output, now);
        var idleDraws = renderer.Draws;

        frame.PointerMotion(70, 45);
        Assert.True(frame.Animating);
        Assert.Equal(now, renderer.LastInteraction.HoverChangedAt);
        Assert.Equal(FramePart.Title, renderer.LastInteraction.Hot);
        Assert.Equal(FramePart.None, renderer.LastInteraction.PreviousHot);
        var hovered = renderer.Draws;
        Assert.Equal(idleDraws + 1, hovered);

        var damaged = 0;
        var requested = 0;
        host.Scene.Damaged += (_, _) => damaged++;
        clock.FrameRequested += () => requested++;
        clock.BeginFrame(host.Output, now + 16_000_000);
        Assert.Equal(hovered + 1, renderer.Draws);
        Assert.Equal(now + 16_000_000, renderer.LastInteraction.Now);
        Assert.True(damaged > 0);
        Assert.Equal(1, requested);

        clock.BeginFrame(host.Output, now + 120_000_000);
        Assert.False(frame.Animating);
        Assert.Equal(1, requested);
        var settled = renderer.Draws;
        clock.BeginFrame(host.Output, now + 140_000_000);
        Assert.Equal(settled, renderer.Draws);

        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = false });
        Assert.Equal(now, renderer.LastInteraction.ActiveChangedAt);
        frame.Dispose();
    }

    [Fact]
    public void A_frame_clock_with_no_prediction_ticks_at_the_frames_own_time()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var renderer = new OutsetFrameRenderer();
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;
        var frame = new Frame(uiHost, renderer, host.Scene.Root) { Clock = clock, Time = () => now };
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();
        clock.BeginFrame(host.Output, 0);

        frame.PointerMotion(70, 45);
        Assert.True(frame.Animating);

        now += 16_000_000;
        clock.BeginFrame(host.Output, 0);
        Assert.Equal(now, renderer.LastInteraction.Now);
        Assert.True(frame.Animating);

        now += 120_000_000;
        clock.BeginFrame(host.Output, 0);
        Assert.Equal(now, renderer.LastInteraction.Now);
        Assert.False(frame.Animating);
        frame.Dispose();
    }

    [Fact]
    public void A_surface_its_host_disposed_hides_the_strips_and_the_next_draw_makes_a_new_one()
    {
        using var host = new CompositorTestHost();
        using var skia = new SkiaUIHost();
        var uiHost = new SurfaceOwningUIHost(skia);
        var frame = new Frame(uiHost, new OutsetFrameRenderer(), host.Scene.Root);
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();
        var lit = host.Pixel(44, 48);
        Assert.NotEqual(0xFF000000u, lit);

        uiHost.DisposeSurfaces();
        host.RenderFrame();
        Assert.Equal(0xFF000000u, host.Pixel(44, 48));
        Assert.False(frame.IsFaulted);

        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();
        Assert.Equal(lit, host.Pixel(44, 48));
        Assert.Equal(1, uiHost.Live);

        frame.Dispose();
        uiHost.DisposeSurfaces();
    }

    [Fact]
    public void A_pending_frame_with_no_buffer_closes_its_fence_on_commit()
    {
        using var host = new CompositorTestHost();
        var fds = new int[2];
        Assert.Equal(0, Pipe(fds));
        try
        {
            using var surface = new FenceOnlyUISurface(fds[0]);
            var frame = new Frame(new FenceOnlyUIHost(surface), new BlankFrameRenderer(), host.Scene.Root);
            frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t" });
            Assert.NotEqual(-1, Fcntl(fds[0], 1));

            frame.Commit();
            Assert.Equal(-1, Fcntl(fds[0], 1));
            frame.Dispose();
        }
        finally
        {
            _ = Close(fds[1]);
        }
    }

    [Fact]
    public void Pointer_fields_track_enter_motion_and_leave()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var renderer = new OutsetFrameRenderer { UsesPointer = true };
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;
        var frame = new Frame(uiHost, renderer, host.Scene.Root) { Clock = clock, Time = () => now };
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();

        frame.PointerEnter(70, 45);
        clock.BeginFrame(host.Output, now);
        Assert.True(renderer.LastInteraction.PointerInside);
        Assert.Equal(40, renderer.LastInteraction.PointerX);
        Assert.Equal(11, renderer.LastInteraction.PointerY);
        Assert.Equal(now, renderer.LastInteraction.PointerChangedAt);

        now += 300_000_000;
        clock.BeginFrame(host.Output, now);
        Assert.False(frame.Animating);

        frame.PointerMotion(72, 46);
        Assert.True(frame.Animating);
        var moved = renderer.Draws;
        clock.BeginFrame(host.Output, now + 16_000_000);
        Assert.Equal(moved + 1, renderer.Draws);
        Assert.Equal(42, renderer.LastInteraction.PointerX);
        Assert.Equal(12, renderer.LastInteraction.PointerY);
        Assert.Equal(now, renderer.LastInteraction.PointerChangedAt);

        now += 300_000_000;
        clock.BeginFrame(host.Output, now);
        frame.PointerLeave();
        Assert.True(frame.Animating);
        clock.BeginFrame(host.Output, now + 16_000_000);
        Assert.False(renderer.LastInteraction.PointerInside);
        Assert.Equal(now, renderer.LastInteraction.PointerChangedAt);
        frame.Dispose();
    }

    [Fact]
    public void Pointer_motion_does_not_redraw_a_renderer_that_ignores_it()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var renderer = new OutsetFrameRenderer();
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;
        var frame = new Frame(uiHost, renderer, host.Scene.Root) { Clock = clock, Time = () => now };
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();

        frame.PointerMotion(70, 45);
        now += 300_000_000;
        clock.BeginFrame(host.Output, now);
        Assert.False(frame.Animating);
        var settled = renderer.Draws;

        for (var i = 0; i < 10; i++)
        {
            frame.PointerMotion(71 + i, 45);
            clock.BeginFrame(host.Output, now + (i * 16_000_000));
        }

        Assert.False(frame.Animating);
        Assert.Equal(settled, renderer.Draws);
        frame.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int Pipe(int[] fds);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
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

internal sealed class OnePartFrameRenderer(FramePart part) : IFrameRenderer
{
    private const int Border = 4;
    private const int TitleHeight = 30;

    private int _outerWidth;
    private int _outerHeight;

    public FrameInsets Measure(in FrameState state, double scale) =>
        new(Border + TitleHeight, Border, Border, Border);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var falsifier = (FalsifierUISurface)surface;
        _outerWidth = clientBox.Width + 2 * Border;
        _outerHeight = clientBox.Height + TitleHeight + 2 * Border;
        _ = falsifier.BeginPixels();
        falsifier.EndPixels();
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale) =>
        x < 0 || y < 0 || x >= _outerWidth || y >= _outerHeight ? FramePart.None
        : y < Border + TitleHeight ? part
        : FramePart.Border;
}

internal sealed class OutsetFrameRenderer : IFrameRenderer
{
    private const int Outset = 6;
    private int _width;
    private int _height;

    public int Draws { get; private set; }

    public Box LastClient { get; private set; }

    public (int Width, int Height) LastSurface { get; private set; }

    public FrameInteraction LastInteraction { get; private set; }

    public FrameState LastState { get; private set; }

    public bool Follows { get; set; }

    public bool FollowsPlacement => Follows;

    public FrameInsets Measure(in FrameState state, double scale) => new(20, 4, 4, 4);

    public FrameInsets Outsets(in FrameState state, double scale) => new(Outset, Outset, Outset, Outset);

    public bool UsesPointer { get; set; }

    public bool Animating(in FrameState state, in FrameInteraction interaction) =>
        (interaction.HoverChangedAt != 0 && interaction.Now - interaction.HoverChangedAt < 100_000_000) ||
        (UsesPointer && interaction.PointerChangedAt != 0 && interaction.Now - interaction.PointerChangedAt < 100_000_000);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        Draws++;
        LastClient = clientBox;
        LastInteraction = interaction;
        LastState = state;
        _width = surface.Size.Width;
        _height = surface.Size.Height;
        LastSurface = (_width, _height);
        var canvas = ((ISkiaUISurface)surface).BeginDraw();
        try
        {
            canvas.Clear(new SKColor(0, 0, 0, 0x40));
            using var paint = new SKPaint { Color = new SKColor(0x28, 0x46, 0x78) };
            canvas.DrawRect(Outset, Outset, _width - 2 * Outset, _height - 2 * Outset, paint);
        }
        finally
        {
            ((ISkiaUISurface)surface).EndDraw();
        }
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale)
    {
        if (x < Outset || y < Outset || x >= _width - Outset || y >= _height - Outset)
        {
            throw new InvalidOperationException("the outsets never reach the renderer");
        }

        if ((int)x == Outset && (int)y == Outset)
        {
            return FramePart.None;
        }

        return y < Outset + 20 ? FramePart.Title : FramePart.Border;
    }

    public Box PartBounds(FramePart part) => part == FramePart.Title ? new Box(Outset, Outset, _width - 2 * Outset, 20) : default;
}

internal sealed class SurfaceOwningUIHost(SkiaUIHost inner) : IUIHost
{
    private readonly List<IUISurface> _made = [];

    public event Action? WakeupRequested
    {
        add => inner.WakeupRequested += value;
        remove => inner.WakeupRequested -= value;
    }

    public int Live => _made.Count;

    public UITargetKind Produces => inner.Produces;

    public Type SurfaceContract => inner.SurfaceContract;

    public long? NextDueMillis => inner.NextDueMillis;

    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        var surface = inner.CreateSurface(options);
        if (surface is not null)
        {
            _made.Add(surface);
        }

        return surface;
    }

    public void Pump() => inner.Pump();

    public void DisposeSurfaces()
    {
        foreach (var surface in _made)
        {
            surface.Dispose();
        }

        _made.Clear();
    }

    public void Dispose() => DisposeSurfaces();
}

internal sealed class FenceOnlyUIHost(IUISurface surface) : IUIHost
{
    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    public UITargetKind Produces => UITargetKind.Memory;

    public long? NextDueMillis => null;

    public IUISurface? CreateSurface(in UISurfaceOptions options) => surface;

    public void Pump()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class FenceOnlyUISurface(int fenceFd) : IUISurface
{
    private int _fenceFd = fenceFd;

    public UISurfaceSize Size => new(64, 64, 1.0);

    public bool Configure(int logicalWidth, int logicalHeight, double scale) => true;

    public bool TryAcquire(out UIFrame frame)
    {
        frame = new UIFrame(default, damage: null, _fenceFd);
        _fenceFd = -1;
        return true;
    }

    public void AddObserver(IUISurfaceObserver observer)
    {
    }

    public void RemoveObserver(IUISurfaceObserver observer)
    {
    }

    public bool AcceptsInputAt(double x, double y) => false;

    public string? CursorAt(double x, double y) => null;

    public void NotifyPointerEnter(double x, double y)
    {
    }

    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
    }

    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
    }

    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
    }

    public void NotifyPointerLeave()
    {
    }

    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    public void Dispose()
    {
    }
}

internal sealed class BlankFrameRenderer : IFrameRenderer
{
    public FrameInsets Measure(in FrameState state, double scale) => new(20, 4, 4, 4);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale) => FramePart.None;
}
