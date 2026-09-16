using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Frames.Metacity;
using Basin.Scene;
using Basin.UI.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class MetacityFrameTests
{
    private const FrameCapabilities AllThree = FrameCapabilities.WindowMenu | FrameCapabilities.Minimize | FrameCapabilities.Maximize;

    internal sealed class Rig : IDisposable
    {
        public Rig(string theme = "Atlanta", int major = 1, string layout = "menu:minimize,maximize,close", MetacityPalette? palette = null)
        {
            Skia = new TestFrameTheme();
            Font = new MetacityFont(Skia.Typeface, 14);
            Resources = new MetacityResources { IconTheme = "no-such-icon-theme" };
            Theme = MetacityTheme.ParseFile(MetacityParserTests.Fixture(theme, major));
            Renderer = new MetacityFrameRenderer(Theme, palette ?? MetacityPalette.Light, MetacityButtonLayout.Parse(layout), Font, Resources);
        }

        public TestFrameTheme Skia { get; }

        public MetacityFont Font { get; }

        public MetacityResources Resources { get; }

        public MetacityTheme Theme { get; }

        public MetacityFrameRenderer Renderer { get; }

        public void Dispose()
        {
            Resources.Dispose();
            Font.Dispose();
            Skia.Dispose();
        }
    }

    public static TheoryData<string, double, string> Goldens => new()
    {
        { "active", 1.0, "metacity-atlanta-active" },
        { "active", 1.5, "metacity-atlanta-active-1.5x" },
        { "active", 2.0, "metacity-atlanta-active-2x" },
        { "inactive", 1.0, "metacity-atlanta-inactive" },
        { "inactive", 1.5, "metacity-atlanta-inactive-1.5x" },
        { "inactive", 2.0, "metacity-atlanta-inactive-2x" },
        { "maximized", 1.0, "metacity-atlanta-maximized" },
        { "shaded", 1.0, "metacity-atlanta-shaded" },
        { "tiled-left", 1.0, "metacity-atlanta-tiled-left" },
        { "dialog", 1.0, "metacity-atlanta-dialog" },
        { "hot-close", 1.0, "metacity-atlanta-hot-close" },
        { "long-title", 1.0, "metacity-atlanta-long-title" },
    };

    internal static FrameState StateFor(string name) => name switch
    {
        "inactive" => new FrameState { Title = "基盤 Basin", Active = false, Capabilities = AllThree },
        "maximized" => new FrameState { Title = "基盤 Basin", Active = true, Maximized = true, Capabilities = AllThree },
        "shaded" => new FrameState { Title = "基盤 Basin", Active = true, Shaded = true, Capabilities = AllThree },
        "tiled-left" => new FrameState { Title = "基盤 Basin", Active = true, Tiled = FrameTiling.Left, Capabilities = AllThree },
        "dialog" => new FrameState { Title = "基盤 Basin", Active = true, Kind = FrameKind.Dialog, Capabilities = AllThree },
        "long-title" => new FrameState { Title = "A window title far too long for the titlebar it is drawn into", Active = true, Capabilities = AllThree },
        _ => new FrameState { Title = "基盤 Basin", Active = true, Capabilities = AllThree },
    };

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Frame_raster_matches_golden(string state, double scale, string golden)
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux(),
            "these goldens carry shaped text, and glyph rasterization belongs to the host's Skia build");
        using var rig = new Rig();
        using var host = new SkiaUIHost();
        var frameState = StateFor(state);
        var insets = rig.Renderer.Measure(frameState, scale);
        const int clientWidth = 200;
        const int clientHeight = 60;
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = clientWidth + insets.Left + insets.Right,
            Height = clientHeight + insets.Top + insets.Bottom,
            Scale = scale,
        });
        Assert.NotNull(surface);

        var interaction = state == "hot-close" ? new FrameInteraction(FramePart.Close, FramePart.None) : default;
        rig.Renderer.Draw(surface, new Box(insets.Left, insets.Top, clientWidth, clientHeight), frameState, interaction);
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
    public void Menu_lists_marco_items_by_capability_and_state()
    {
        using var rig = new Rig();
        var all = AllThree | FrameCapabilities.Shade | FrameCapabilities.Above | FrameCapabilities.Stick;
        var state = new FrameState { Title = "t", Active = true, Capabilities = all, Maximized = true, Sticky = true };
        var size = rig.Renderer.MeasureMenu(state, 1.0);
        Assert.True(size.Width > 0 && size.Height > 0);

        var actions = new List<FrameActionKind?>();
        var seen = new HashSet<int>();
        var separators = 0;
        for (var y = 0; y < size.Height; y++)
        {
            var item = rig.Renderer.MenuItemAt(size.Width / 2, y, state, 1.0);
            if (item < 0)
            {
                separators++;
                continue;
            }

            var action = rig.Renderer.MenuItemAction(item, state)?.Kind;
            if (seen.Add(item))
            {
                actions.Add(action);
            }
        }

        Assert.Equal(
            [FrameActionKind.Minimize, FrameActionKind.ToggleMaximize, FrameActionKind.ToggleShade, FrameActionKind.Move, FrameActionKind.Resize, FrameActionKind.ToggleAbove, null, FrameActionKind.ToggleSticky, FrameActionKind.Close],
            actions);
        Assert.True(separators > 0);

        var bare = new FrameState { Title = "t", Active = true, Capabilities = FrameCapabilities.None };
        var bareSize = rig.Renderer.MeasureMenu(bare, 1.0);
        Assert.True(bareSize.Height < size.Height);
        Assert.Equal(FrameActionKind.Move, rig.Renderer.MenuItemAction(0, bare)?.Kind);
        Assert.Null(rig.Renderer.MenuItemAction(99, bare));
        Assert.Equal(-1, rig.Renderer.MenuItemAt(-1, 10, bare, 1.0));
    }

    [Fact]
    public void Menu_raster_matches_golden()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the golden carries shaped text");
        using var rig = new Rig();
        using var host = new SkiaUIHost();
        var state = new FrameState { Title = "t", Active = true, Capabilities = AllThree | FrameCapabilities.Shade | FrameCapabilities.Above | FrameCapabilities.Stick, Above = true };
        var size = rig.Renderer.MeasureMenu(state, 1.0);
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = size.Width, Height = size.Height, Scale = 1.0 });
        Assert.NotNull(surface);
        rig.Renderer.DrawMenu(surface, state, hotItem: 2);
        Assert.True(surface.TryAcquire(out var frame));
        try
        {
            Golden.AssertMatches((MemoryBuffer)frame.Buffer!, "metacity-atlanta-menu");
        }
        finally
        {
            frame.Dispose();
            surface.Dispose();
        }
    }

    [Fact]
    public void PartAt_agrees_with_PartBounds_for_every_button()
    {
        using var rig = new Rig(layout: "menu,shade:minimize,maximize,close");
        using var host = new SkiaUIHost();
        var state = new FrameState { Title = "t", Active = true, Capabilities = AllThree | FrameCapabilities.Shade };
        var insets = rig.Renderer.Measure(state, 1.0);
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 300 + insets.Left + insets.Right, Height = 100 + insets.Top + insets.Bottom, Scale = 1.0 });
        Assert.NotNull(surface);
        rig.Renderer.Draw(surface, new Box(insets.Left, insets.Top, 300, 100), state, default);

        foreach (var part in new[] { FramePart.Menu, FramePart.Shade, FramePart.Minimize, FramePart.Maximize, FramePart.Close })
        {
            var bounds = rig.Renderer.PartBounds(part);
            Assert.False(bounds.IsEmpty, part.ToString());
            Assert.Equal(part, rig.Renderer.PartAt(bounds.X, bounds.Y + 4, state, 1.0));
            Assert.Equal(part, rig.Renderer.PartAt(bounds.Right - 1, bounds.Bottom - 1, state, 1.0));
            Assert.NotEqual(part, rig.Renderer.PartAt(bounds.Right, bounds.Y + 4, state, 1.0));
        }

        var width = 300 + insets.Left + insets.Right;
        Assert.Equal(FramePart.Top, rig.Renderer.PartAt(150, 2, state, 1.0));
        Assert.Equal(FramePart.Title, rig.Renderer.PartAt(150, 6, state, 1.0));
        Assert.Equal(FramePart.Title, rig.Renderer.PartAt(150, insets.Top - 1, state, 1.0));
        Assert.Equal(FramePart.TopLeft, rig.Renderer.PartAt(1, 1, state, 1.0));
        Assert.Equal(FramePart.TopLeft, rig.Renderer.PartAt(rig.Renderer.PartBounds(FramePart.Menu).X + 2, 1, state, 1.0));
        Assert.Equal(FramePart.TopLeft, rig.Renderer.PartAt(1, 12, state, 1.0));
        Assert.Equal(FramePart.TopRight, rig.Renderer.PartAt(width - 1, 1, state, 1.0));
        Assert.Equal(FramePart.TopRight, rig.Renderer.PartAt(rig.Renderer.PartBounds(FramePart.Close).Right - 1, 1, state, 1.0));
        Assert.Equal(FramePart.TopRight, rig.Renderer.PartAt(width - 1, 12, state, 1.0));
        Assert.Equal(FramePart.Left, rig.Renderer.PartAt(1, insets.Top + 40, state, 1.0));
        Assert.Equal(FramePart.Right, rig.Renderer.PartAt(300 + insets.Left + 2, insets.Top + 40, state, 1.0));
        Assert.Equal(FramePart.Bottom, rig.Renderer.PartAt(150, insets.Top + 100 + 2, state, 1.0));
        Assert.Equal(FramePart.BottomLeft, rig.Renderer.PartAt(1, insets.Top + 100 + 2, state, 1.0));
        Assert.Equal(FramePart.BottomRight, rig.Renderer.PartAt(300 + insets.Left + 2, insets.Top + 100 + 2, state, 1.0));
        Assert.Equal(FramePart.TopLeft, rig.Renderer.PartAt(1, insets.Top + 2, state, 1.0));
        Assert.Equal(FramePart.None, rig.Renderer.PartAt(-1, 5, state, 1.0));
        Assert.Equal(default, rig.Renderer.PartBounds(FramePart.Title));
        Assert.Equal("top_left_corner", rig.Renderer.CursorFor(FramePart.TopLeft));
        Assert.Null(rig.Renderer.CursorFor(FramePart.Close));
        Assert.True(rig.Renderer.OpaqueChrome);
        surface.Dispose();
    }

    [Fact]
    public void Rounded_corners_make_the_chrome_translucent()
    {
        using var rig = new Rig(theme: "eOS", major: 3);
        using var host = new SkiaUIHost();
        var state = new FrameState { Title = "t", Active = true, Capabilities = AllThree };
        var insets = rig.Renderer.Measure(state, 1.0);
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 200 + insets.Left + insets.Right, Height = 60 + insets.Top + insets.Bottom, Scale = 1.0 });
        Assert.NotNull(surface);
        rig.Renderer.Draw(surface, new Box(insets.Left, insets.Top, 200, 60), state, default);
        Assert.False(rig.Renderer.OpaqueChrome);
        Assert.True(surface.TryAcquire(out var frame));
        var pixels = BufferCapture.ReadRgba((MemoryBuffer)frame.Buffer!);
        Assert.Equal(0, pixels[3]);
        var width = frame.Buffer!.Width;
        Assert.Equal(0, pixels[(width - 1) * 4 + 3]);
        Assert.NotEqual(0, pixels[(10 * width + 10) * 4 + 3]);
        frame.Dispose();
        surface.Dispose();
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.7)]
    [InlineData(2.0)]
    public void Rounded_corners_are_cleared_in_device_pixels(double scale)
    {
        using var rig = new Rig(theme: "eOS", major: 3);
        using var host = new SkiaUIHost();
        var state = new FrameState { Title = "t", Active = true, Capabilities = AllThree };
        var insets = rig.Renderer.Measure(state, scale);
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 200 + insets.Left + insets.Right, Height = 60 + insets.Top + insets.Bottom, Scale = scale });
        Assert.NotNull(surface);
        rig.Renderer.Draw(surface, new Box(insets.Left, insets.Top, 200, 60), state, default);
        Assert.True(surface.TryAcquire(out var frame));
        var buffer = (MemoryBuffer)frame.Buffer!;
        var pixels = BufferCapture.ReadRgba(buffer);
        var width = buffer.Width;
        var corner = (int)(rig.Renderer.Geometry.TopLeftRadius * scale);
        Assert.True(corner > 0);
        var r = Math.Sqrt(corner) + corner;
        for (var i = 0; i < corner; i++)
        {
            var cleared = (int)Math.Floor(0.5 + r - Math.Sqrt(r * r - (r - (i + 0.5)) * (r - (i + 0.5))));
            for (var x = 0; x < cleared; x++)
            {
                Assert.Equal(0, pixels[(i * width + x) * 4 + 3]);
                Assert.Equal(0, pixels[(i * width + width - 1 - x) * 4 + 3]);
            }

            Assert.NotEqual(0, pixels[(i * width + cleared) * 4 + 3]);
        }

        Assert.NotEqual(0, pixels[(corner * width) * 4 + 3]);
        frame.Dispose();
        surface.Dispose();
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.7)]
    [InlineData(2.0)]
    public void Images_land_on_whole_device_pixels_at_any_scale(double scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"basin-metacity-image-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using (var bitmap = new SkiaSharp.SKBitmap(4, 4, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
            {
                bitmap.Erase(new SkiaSharp.SKColor(0x20, 0x40, 0x80));
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(directory, "solid.png"), data.ToArray());
            }

            var text = MetacityParserTests.Wrap(
                MetacityParserTests.Geometry
                + "<draw_ops name=\"empty\"/>"
                + "<draw_ops name=\"bar\"><image filename=\"solid.png\" x=\"3\" y=\"2\" width=\"width - 6\" height=\"height - 4\"/></draw_ops>"
                + MetacityParserTests.MinimalStyle.Replace("<frame_style name=\"s\" geometry=\"g\">", "<frame_style name=\"s\" geometry=\"g\"><piece position=\"titlebar\" draw_ops=\"bar\"/>", StringComparison.Ordinal));
            MetacityTheme theme;
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
            {
                theme = MetacityTheme.Parse(stream, directory, 1, "solid");
            }

            using var skia = new TestFrameTheme();
            using var font = new MetacityFont(skia.Typeface, 14);
            using var resources = new MetacityResources();
            var renderer = new MetacityFrameRenderer(theme, MetacityPalette.Light, MetacityButtonLayout.Parse(string.Empty), font, resources);
            using var host = new SkiaUIHost();
            var state = new FrameState { Title = "t", Active = true };
            var insets = renderer.Measure(state, scale);
            var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 100 + insets.Left + insets.Right, Height = 40 + insets.Top + insets.Bottom, Scale = scale });
            Assert.NotNull(surface);
            renderer.Draw(surface, new Box(insets.Left, insets.Top, 100, 40), state, default);
            Assert.True(surface.TryAcquire(out var frame));
            var buffer = (MemoryBuffer)frame.Buffer!;
            var pixels = BufferCapture.ReadRgba(buffer);
            var width = buffer.Width;
            var expected = OutputScaling.ToPhysical(new Box(3, 2, renderer.Geometry.Width - 6, insets.Top - 4), scale);

            uint At(int x, int y)
            {
                var i = (y * width + x) * 4;
                return (uint)(pixels[i] << 24 | pixels[i + 1] << 16 | pixels[i + 2] << 8 | pixels[i + 3]);
            }

            const uint solid = 0x204080FFu;
            for (var y = expected.Y; y < expected.Bottom; y++)
            {
                Assert.Equal(solid, At(expected.X, y));
                Assert.Equal(solid, At(expected.Right - 1, y));
                Assert.NotEqual(solid, At(expected.X - 1, y));
                Assert.NotEqual(solid, At(expected.Right, y));
            }

            for (var x = expected.X; x < expected.Right; x++)
            {
                Assert.Equal(solid, At(x, expected.Y));
                Assert.Equal(solid, At(x, expected.Bottom - 1));
                Assert.NotEqual(solid, At(x, expected.Y - 1));
                Assert.NotEqual(solid, At(x, expected.Bottom));
            }

            frame.Dispose();
            surface.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Hover_repaints_the_button_rectangle_only()
    {
        using var host = new CompositorTestHost();
        using var rig = new Rig();
        using var uiHost = new SkiaUIHost();
        var frame = new Frame(uiHost, rig.Renderer, host.Scene.Root);
        var geometry = new Box(20, 40, 200, 60);
        frame.Configure(geometry, 1.0, StateFor("active"));
        frame.Commit();
        host.RenderFrame();

        var damagedArea = 0L;
        host.Scene.Damaged += (_, box) => damagedArea += (long)box.Width * box.Height;
        var close = rig.Renderer.PartBounds(FramePart.Close);
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

            using var rig = new Rig();
            using var uiHost = new SkiaUIHost();
            var frame = new Frame(uiHost, rig.Renderer, host.Scene.Root);
            var geometry = new Box(20, 40, 120, 48);
            var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
            client.SetPosition(geometry.X, geometry.Y);

            frame.Configure(geometry, scale, StateFor("active"));
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

            Golden.AssertMatches(host, renderer == "pixman" ? $"metacity-seam{suffix}" : $"metacity-seam{suffix}-{renderer}");
        }
    }

    [Fact]
    public void An_idle_decorated_window_allocates_nothing()
    {
        using var host = new CompositorTestHost();
        using var rig = new Rig();
        using var uiHost = new SkiaUIHost();
        var frame = new Frame(uiHost, rig.Renderer, host.Scene.Root);
        var geometry = new Box(20, 40, 200, 60);
        var client = new SceneRect(host.Scene.Root, geometry.Width, geometry.Height, new RenderColor(0.8f, 0.5f, 0.2f, 1f));
        client.SetPosition(geometry.X, geometry.Y);
        frame.Configure(geometry, 1.0, StateFor("active"));
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

        var close = rig.Renderer.PartBounds(FramePart.Close);
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
    public void Frame_lifetime_leaves_counters_clean()
    {
        using var host = new CompositorTestHost();
        using (var rig = new Rig(theme: "eOS", major: 3))
        using (var uiHost = new SkiaUIHost())
        {
            var frame = new Frame(uiHost, rig.Renderer, host.Scene.Root);
            frame.Configure(new Box(20, 40, 120, 48), 1.0, StateFor("active"));
            frame.Commit();
            host.RenderFrame();
            frame.Configure(new Box(20, 40, 160, 60), 2.0, StateFor("inactive"));
            frame.Commit();
            host.RenderFrame();
            frame.PointerMotion(20 + 160 - 8, 40 - 15);
            host.RenderFrame();
            frame.Dispose();
        }

        host.RenderFrame();
    }
}
