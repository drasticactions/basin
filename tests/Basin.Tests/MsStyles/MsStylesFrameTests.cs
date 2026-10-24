using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Frames.MsStyles;
using Basin.Scene;
using Basin.UI.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class MsStylesFrameTests
{
    public static TheoryData<string, MsStylesLook, string, double, string> Goldens => new()
    {
        { "Plain", MsStylesLook.ClassicXp, "active", 1.0, "msstyles-plain-active" },
        { "Plain", MsStylesLook.ClassicXp, "active", 1.5, "msstyles-plain-active-1.5x" },
        { "Plain", MsStylesLook.ClassicXp, "active", 2.0, "msstyles-plain-active-2x" },
        { "Plain", MsStylesLook.ClassicXp, "inactive", 1.0, "msstyles-plain-inactive" },
        { "Plain", MsStylesLook.ClassicXp, "inactive", 1.5, "msstyles-plain-inactive-1.5x" },
        { "Plain", MsStylesLook.ClassicXp, "inactive", 2.0, "msstyles-plain-inactive-2x" },
        { "Plain", MsStylesLook.ClassicXp, "maximized", 1.0, "msstyles-plain-maximized" },
        { "Plain", MsStylesLook.ClassicXp, "utility", 1.0, "msstyles-plain-utility" },
        { "Plain", MsStylesLook.ClassicXp, "no-minimize", 1.0, "msstyles-plain-no-minimize" },
        { "Glassy", MsStylesLook.Basic, "active", 1.0, "msstyles-glassy-basic-active" },
        { "Glassy", MsStylesLook.Basic, "active", 1.5, "msstyles-glassy-basic-active-1.5x" },
        { "Glassy", MsStylesLook.Basic, "active", 2.0, "msstyles-glassy-basic-active-2x" },
        { "Glassy", MsStylesLook.Basic, "inactive", 1.0, "msstyles-glassy-basic-inactive" },
        { "Glassy", MsStylesLook.Basic, "inactive", 1.5, "msstyles-glassy-basic-inactive-1.5x" },
        { "Glassy", MsStylesLook.Basic, "inactive", 2.0, "msstyles-glassy-basic-inactive-2x" },
        { "Glassy", MsStylesLook.Basic, "maximized", 1.0, "msstyles-glassy-basic-maximized" },
        { "Glassy", MsStylesLook.Basic, "utility", 1.0, "msstyles-glassy-basic-utility" },
        { "Glassy", MsStylesLook.Glass, "active", 1.0, "msstyles-glassy-glass-active" },
        { "Glassy", MsStylesLook.Glass, "active", 1.5, "msstyles-glassy-glass-active-1.5x" },
        { "Glassy", MsStylesLook.Glass, "active", 2.0, "msstyles-glassy-glass-active-2x" },
        { "Glassy", MsStylesLook.Glass, "inactive", 1.0, "msstyles-glassy-glass-inactive" },
        { "Glassy", MsStylesLook.Glass, "inactive", 1.5, "msstyles-glassy-glass-inactive-1.5x" },
        { "Glassy", MsStylesLook.Glass, "inactive", 2.0, "msstyles-glassy-glass-inactive-2x" },
        { "Glassy", MsStylesLook.Glass, "maximized", 1.0, "msstyles-glassy-glass-maximized" },
        { "Glassy", MsStylesLook.Glass, "utility", 1.0, "msstyles-glassy-glass-utility" },
        { "Glassy", MsStylesLook.Glass, "dialog", 1.0, "msstyles-glassy-glass-dialog" },
    };

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Frame_raster_matches_golden(string theme, MsStylesLook look, string state, double scale, string golden)
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux(),
            "these goldens carry shaped text, and glyph rasterization belongs to the host's Skia build");
        using var rig = new MsStylesRig(theme, look);
        using var host = new SkiaUIHost();
        var renderer = rig.Renderer();
        var frameState = MsStylesRig.StateFor(state);
        var insets = renderer.Measure(frameState, scale);
        var outsets = renderer.Outsets(frameState, scale);
        const int clientWidth = 200;
        const int clientHeight = 40;
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = clientWidth + insets.Left + insets.Right + outsets.Left + outsets.Right,
            Height = clientHeight + insets.Top + insets.Bottom + outsets.Top + outsets.Bottom,
            Scale = scale,
        });
        Assert.NotNull(surface);

        renderer.Draw(surface, new Box(insets.Left + outsets.Left, insets.Top + outsets.Top, clientWidth, clientHeight), frameState, default);
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
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void A_maximized_glass_caption_is_opaque(double scale)
    {
        using var rig = new MsStylesRig("Glassy", MsStylesLook.Glass);
        using var host = new SkiaUIHost();
        var renderer = rig.Renderer();
        var state = MsStylesRig.StateFor("maximized");
        var (insets, outsets) = (renderer.Measure(state, scale), renderer.Outsets(state, scale));
        Assert.Equal(default, outsets);
        Assert.Equal((0, 0, 0), (insets.Left, insets.Right, insets.Bottom));
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 200, Height = 40 + insets.Top, Scale = scale })!;
        renderer.Draw(surface, new Box(0, insets.Top, 200, 40), state, default);
        Assert.True(renderer.OpaqueChrome);
        Assert.True(surface.TryAcquire(out var frame));
        try
        {
            var buffer = frame.Buffer!;
            Assert.True(BufferCapture.TryReadRgba(buffer, out var rgba));
            AssertStripOpaque(rgba!, buffer.Width, OutputScaling.ToPhysical(new Box(0, 0, 200, insets.Top), scale));
        }
        finally
        {
            frame.Dispose();
            surface.Dispose();
        }
    }

    [Theory]
    [InlineData("gl", 1.0)]
    [InlineData("gl", 1.5)]
    [InlineData("vulkan", 1.0)]
    [InlineData("vulkan", 1.5)]
    public void A_maximized_quill_glass_caption_is_opaque(string row, double scale)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, row);
        Assert.SkipWhen(lease is null, $"this row builds no {row} quill host");
        using var rig = new MsStylesRig("Glassy", MsStylesLook.Glass);
        using var text = new Basin.Frames.MsStyles.Quill.MsStylesQuillText(rig.Fonts) { Fallback = Basin.Frames.Quill.QuillFrameFonts.Bundled() };
        var renderer = new Basin.Frames.MsStyles.Quill.MsStylesQuillFrameRenderer(rig.Style, new Basin.Frames.MsStyles.Quill.MsStylesQuillPainter(rig.Style.Images, text));
        var state = MsStylesRig.StateFor("maximized");
        var insets = renderer.Measure(state, scale);
        Assert.Equal(default, renderer.Outsets(state, scale));
        var surface = lease!.Host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Dmabuf, Width = 200, Height = 40 + insets.Top, Scale = scale })!;
        renderer.Draw(surface, new Box(0, insets.Top, 200, 40), state, default);
        Assert.True(renderer.OpaqueChrome);
        Assert.True(surface.TryAcquire(out var frame));
        try
        {
            var buffer = frame.Buffer!;
            Assert.True(BufferCapture.TryReadRgba(buffer, host.Renderer, out var rgba));
            AssertStripOpaque(rgba!, buffer.Width, OutputScaling.ToPhysical(new Box(0, 0, 200, insets.Top), scale));
        }
        finally
        {
            frame.Dispose();
            surface.Dispose();
        }
    }

    private static void AssertStripOpaque(byte[] rgba, int width, Box strip)
    {
        for (var y = strip.Y; y < strip.Bottom; y++)
        {
            for (var x = strip.X; x < strip.Right; x++)
            {
                var alpha = rgba[((y * width) + x) * 4 + 3];
                if (alpha != 255)
                {
                    Assert.Fail($"alpha {alpha} at ({x},{y}) in the caption strip {strip}");
                }
            }
        }
    }

    [Theory]
    [InlineData(FramePart.Close, false, "msstyles-plain-hot-close")]
    [InlineData(FramePart.Minimize, true, "msstyles-plain-pressed-minimize")]
    public void Button_states_match_golden(FramePart part, bool pressed, string golden)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "these goldens carry shaped text");
        using var rig = new MsStylesRig();
        using var host = new SkiaUIHost();
        var renderer = rig.Renderer();
        var state = MsStylesRig.StateFor("active");
        var insets = renderer.Measure(state, 1.0);
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 208, Height = 40 + insets.Top + insets.Bottom, Scale = 1.0 })!;
        renderer.Draw(surface, new Box(insets.Left, insets.Top, 200, 40), state, new FrameInteraction(part, pressed ? part : FramePart.None));
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

    [Fact]
    public void Hover_repaints_the_button_rectangle_only()
    {
        using var host = new CompositorTestHost();
        using var rig = new MsStylesRig();
        using var uiHost = new SkiaUIHost();
        var renderer = rig.Renderer();
        var frame = new Frame(uiHost, renderer, host.Scene.Root);
        var geometry = new Box(20, 40, 200, 60);
        frame.Configure(geometry, 1.0, MsStylesRig.StateFor("active"));
        frame.Commit();
        host.RenderFrame();

        var damagedArea = 0L;
        host.Scene.Damaged += (_, box) => damagedArea += (long)box.Width * box.Height;
        var close = renderer.PartBounds(FramePart.Close);
        var titleY = 40 - frame.Insets.Top + close.Y + 1;
        for (var x = 60; x < 120; x += 3)
        {
            frame.PointerMotion(x, titleY);
        }

        var titleSweep = damagedArea;
        frame.PointerMotion(20 - frame.Insets.Left + close.X + 1, titleY);
        frame.PointerMotion(90, titleY);
        var buttonDamage = damagedArea - titleSweep;
        frame.Dispose();

        Assert.Equal(0, titleSweep);
        Assert.InRange(buttonDamage, 1, 2L * close.Width * close.Height);
    }

    public static TheoryData<string> Renderers => UIFrameTests.Renderers;

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

            using var rig = new MsStylesRig("Glassy", MsStylesLook.Basic);
            using var uiHost = new SkiaUIHost();
            var frame = new Frame(uiHost, rig.Renderer(), host.Scene.Root);
            var geometry = new Box(20, 40, 120, 48);
            var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
            client.SetPosition(geometry.X, geometry.Y);

            frame.Configure(geometry, scale, MsStylesRig.StateFor("untitled"));
            frame.Commit();
            host.RenderFrame();

            var insets = frame.Insets;
            frame.Dispose();
            client.Destroy();

            var outer = OutputScaling.ToPhysical(
                new Box(
                    geometry.X - insets.Left,
                    geometry.Y - insets.Top + 3,
                    geometry.Width + insets.Left + insets.Right,
                    geometry.Height + insets.Top + insets.Bottom - 3),
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

            Golden.AssertMatches(host, renderer == "pixman" ? $"msstyles-seam{suffix}" : $"msstyles-seam{suffix}-{renderer}");
        }
    }

    [Fact]
    public void An_idle_decorated_window_allocates_nothing()
    {
        using var host = new CompositorTestHost();
        using var rig = new MsStylesRig();
        using var uiHost = new SkiaUIHost();
        var renderer = rig.Renderer();
        var frame = new Frame(uiHost, renderer, host.Scene.Root);
        var geometry = new Box(20, 40, 200, 60);
        var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
        client.SetPosition(geometry.X, geometry.Y);
        frame.Configure(geometry, 1.0, MsStylesRig.StateFor("plain"));
        frame.Commit();
        for (var i = 0; i < 20; i++)
        {
            host.RenderFrame();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            host.RenderFrame();
        }

        var idle = GC.GetAllocatedBytesForCurrentThread() - before;

        var close = renderer.PartBounds(FramePart.Close);
        var hotX = 20 - frame.Insets.Left + close.X + 1;
        var hotY = 40 - frame.Insets.Top + close.Y + 1;
        for (var i = 0; i < 20; i++)
        {
            frame.PointerMotion(hotX, hotY);
            frame.PointerMotion(90, hotY);
        }

        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            frame.PointerMotion(hotX, hotY);
            frame.PointerMotion(90, hotY);
        }

        var hover = GC.GetAllocatedBytesForCurrentThread() - before;
        frame.Dispose();
        client.Destroy();

        Assert.Equal(0, idle);
        Assert.Equal(0, hover);
    }

    [Fact]
    public void An_idle_and_hovered_glass_window_allocates_nothing()
    {
        using var host = new CompositorTestHost();
        using var rig = new MsStylesRig("Glassy", MsStylesLook.Glass);
        using var uiHost = new SkiaUIHost();
        var renderer = rig.Renderer();
        var frame = new Frame(uiHost, renderer, host.Scene.Root);
        var geometry = new Box(40, 60, 200, 60);
        frame.Configure(geometry, 1.0, MsStylesRig.StateFor("untitled"));
        frame.Commit();
        Assert.Equal(new FrameInsets(14, 20, 20, 14), frame.Outsets);
        Assert.Equal(new Box(18, 18, 250, 130), frame.DrawnBounds);
        for (var i = 0; i < 20; i++)
        {
            host.RenderFrame();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            host.RenderFrame();
        }

        var idle = GC.GetAllocatedBytesForCurrentThread() - before;
        var close = renderer.Painter.Geometry.BoundsOf(FramePart.Close);
        var hotX = frame.DrawnBounds.X + (int)close.X + 1;
        var hotY = frame.DrawnBounds.Y + (int)close.Y + 1;
        for (var i = 0; i < 20; i++)
        {
            frame.PointerMotion(hotX, hotY);
            frame.PointerMotion(100, hotY);
        }

        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            frame.PointerMotion(hotX, hotY);
            frame.PointerMotion(100, hotY);
        }

        var hover = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(FramePart.None, frame.PartAt(19, 80));
        Assert.Equal(FramePart.Close, frame.PartAt(hotX, hotY));
        frame.Dispose();

        Assert.Equal(0, idle);
        Assert.Equal(0, hover);
    }

    [Fact]
    public void Frame_lifetime_leaves_counters_clean()
    {
        using var host = new CompositorTestHost();
        using (var rig = new MsStylesRig("Glassy", MsStylesLook.Basic))
        using (var uiHost = new SkiaUIHost())
        {
            var frame = new Frame(uiHost, rig.Renderer(), host.Scene.Root);
            frame.Configure(new Box(20, 40, 120, 48), 1.0, MsStylesRig.StateFor("active"));
            frame.Commit();
            host.RenderFrame();
            frame.Configure(new Box(20, 40, 160, 60), 2.0, MsStylesRig.StateFor("inactive"));
            frame.Commit();
            host.RenderFrame();
            frame.PointerMotion(20 + 160 - 8, 40 - 15);
            host.RenderFrame();
            frame.Dispose();
        }

        host.RenderFrame();
    }

    [Fact]
    public void Glass_on_an_xp_theme_is_refused_by_name()
    {
        var theme = Basin.Themes.MsStyles.MsStylesTheme.Parse(MsStylesWriter.Plain());
        var error = Assert.Throws<ArgumentException>(() => new MsStylesFrameStyle(theme, MsStylesLook.Glass));
        Assert.Contains("PACKTHEM 3", error.Message);
    }
}
