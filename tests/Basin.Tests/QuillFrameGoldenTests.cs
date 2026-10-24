using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Frames.Quill;
using Basin.UI.Quill;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class QuillFrameGoldenTests
{
    [Fact]
    public void Golden_quill_frame() => AssertGolden("gl", "gl", "quill-frame-gl");

    [Fact]
    public void Golden_quill_frame_on_vulkan() => AssertGolden("vulkan", "vulkan", "quill-frame-vk");

    [Fact]
    public void A_rounded_frame_is_not_opaque_chrome()
    {
        var rounded = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled(), CornerRadius = 8f });
        var square = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled(), CornerRadius = 0f });
        var state = new FrameState { Title = "x", Capabilities = FrameCapabilities.Maximize };

        Assert.False(rounded.OpaqueChrome);
        Assert.False(square.OpaqueChrome);

        LayoutOnly(rounded, state);
        LayoutOnly(square, state);

        Assert.False(rounded.OpaqueChrome);
        Assert.True(square.OpaqueChrome);
    }

    [Fact]
    public void The_parts_the_paint_used_are_the_parts_the_hit_test_reads()
    {
        var renderer = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled() });
        var state = new FrameState
        {
            Title = "hit",
            Active = true,
            Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize | FrameCapabilities.WindowMenu,
        };

        LayoutOnly(renderer, state);
        var geometry = renderer.Geometry;

        Assert.Equal(FramePart.Close, At(renderer, geometry.Close, state));
        Assert.Equal(FramePart.Maximize, At(renderer, geometry.Maximize, state));
        Assert.Equal(FramePart.Minimize, At(renderer, geometry.Minimize, state));
        Assert.Equal(FramePart.Menu, At(renderer, geometry.Menu, state));
        Assert.Equal(FramePart.Title, At(renderer, geometry.Title, state));
        Assert.Equal(FramePart.TopLeft, renderer.PartAt(1, 1, state, 1.0));
        Assert.Equal(FramePart.BottomRight, renderer.PartAt(geometry.Width - 2, geometry.Height - 2, state, 1.0));
        Assert.Equal("bottom_right_corner", renderer.CursorFor(FramePart.BottomRight));
        Assert.Equal(geometry.Close, renderer.PartBounds(FramePart.Close));
    }

    [Fact]
    public void The_menu_lists_only_what_the_capabilities_allow()
    {
        var renderer = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled() });
        var bare = new FrameState { Title = "bare" };
        var full = new FrameState
        {
            Title = "full",
            Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize | FrameCapabilities.Shade,
        };

        var bareSize = renderer.MeasureMenu(bare, 1.0);
        var fullSize = renderer.MeasureMenu(full, 1.0);
        Assert.True(fullSize.Height > bareSize.Height);

        Assert.Equal(0, renderer.MenuItemAt(10, bareSize.Height / 2.0, bare, 1.0));
        Assert.Equal(FrameActionKind.Close, renderer.MenuItemAction(0, bare)!.Value.Kind);
        Assert.Equal(FrameActionKind.Minimize, renderer.MenuItemAction(0, full)!.Value.Kind);
        Assert.Null(renderer.MenuItemAction(9, bare));
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void The_window_menu_follows_the_color_scheme(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, row);
        Assert.SkipWhen(lease is null, $"this row builds no {row} quill host");

        var dark = new QuillFrameTheme { Face = QuillFrameFonts.Bundled(), MenuFill = new Prowl.Vector.Color32(0xC0, 0x20, 0x30, 0xFF) };
        var renderer = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled() }) { DarkTheme = dark };
        var state = new FrameState { Title = "menu", ColorScheme = FrameColorScheme.Dark, Capabilities = FrameCapabilities.Maximize };

        var size = renderer.MeasureMenu(state, 1.0);
        var surface = (IQuillUISurface)lease!.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = size.Width,
            Height = size.Height,
            Scale = 1.0,
        })!;
        renderer.DrawMenu(surface, state, -1);
        Assert.True(surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, host.Renderer, out var rgba));
        var at = ((buffer.Height / 2 * buffer.Width) + buffer.Width - 6) * 4;
        Assert.Equal((0xC0, 0x20, 0x30), (rgba![at], rgba[at + 1], rgba[at + 2]));

        frame.Dispose();
        surface.Dispose();
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void A_dark_theme_face_draws_the_dark_title(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        const string serif = "/usr/share/fonts/liberation/LiberationSerif-Bold.ttf";
        Assert.SkipWhen(!File.Exists(serif), $"no {serif}");
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, row);
        Assert.SkipWhen(lease is null, $"this row builds no {row} quill host");

        Prowl.Scribe.FontFile face;
        using (var stream = File.OpenRead(serif))
        {
            face = new Prowl.Scribe.FontFile(stream);
        }

        var renderer = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled() })
        {
            DarkTheme = new QuillFrameTheme { Face = face },
        };

        byte[] Title(FrameColorScheme scheme)
        {
            var state = new FrameState { Title = "Quill frame", Active = true, ColorScheme = scheme };
            var client = new Box(0, 0, 240, 100);
            var insets = renderer.Measure(state, 1.0);
            var surface = (IQuillUISurface)lease!.Host.CreateSurface(new UISurfaceOptions
            {
                Target = UITargetKind.Dmabuf,
                Width = client.Width + insets.Left + insets.Right,
                Height = client.Height + insets.Top + insets.Bottom,
                Scale = 1.0,
            })!;
            renderer.Draw(surface, new Box(insets.Left, insets.Top, client.Width, client.Height), state, default);
            Assert.True(surface.TryAcquire(out var frame));
            Assert.True(BufferCapture.TryReadRgba(frame.Buffer!, host.Renderer, out var rgba));
            frame.Dispose();
            surface.Dispose();
            return rgba!;
        }

        var light = Title(FrameColorScheme.Light);
        var darkTitle = Title(FrameColorScheme.Dark);
        Assert.Equal(light.Length, darkTitle.Length);
        Assert.False(light.AsSpan().SequenceEqual(darkTitle), "the dark title must draw in the dark theme's face");
    }

    private static void AssertGolden(string row, string backend, string golden)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");

        var theme = new QuillFrameTheme { Face = QuillFrameFonts.Bundled() };
        var renderer = new QuillFrameRenderer(theme);
        var state = new FrameState
        {
            Title = "Quill frame",
            AppId = "basin.quill",
            Active = true,
            Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize | FrameCapabilities.WindowMenu,
        };

        var client = new Box(0, 0, 240, 100);
        var insets = renderer.Measure(state, 1.0);
        var surface = (IQuillUISurface)lease!.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = client.Width + insets.Left + insets.Right,
            Height = client.Height + insets.Top + insets.Bottom,
            Scale = 1.0,
        })!;

        renderer.Draw(surface, new Box(insets.Left, insets.Top, client.Width, client.Height), state, default);
        Assert.True(surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, host.Renderer, out var rgba));
        Golden.AssertMatches(rgba!, buffer.Width, buffer.Height, golden, tolerance: 6);

        frame.Dispose();
        surface.Dispose();
    }

    private static FramePart At(QuillFrameRenderer renderer, in Box box, in FrameState state) =>
        renderer.PartAt(box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0), state, 1.0);

    private static void LayoutOnly(QuillFrameRenderer renderer, in FrameState state)
    {
        var insets = renderer.Measure(state, 1.0);
        var surface = new LayoutOnlySurface(240 + insets.Left + insets.Right, 100 + insets.Top + insets.Bottom);
        try
        {
            renderer.Draw(surface, new Box(insets.Left, insets.Top, 240, 100), state, default);
        }
        catch (InvalidOperationException)
        {
        }
    }

}
