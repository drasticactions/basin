using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Frames.Quill;
using Basin.UI.Quill;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class QuillGlassFrameTests
{
    private const long Ms = 1_000_000;

    private static readonly FrameState State = new()
    {
        Title = "Glass",
        AppId = "basin.glass",
        Active = true,
        Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize | FrameCapabilities.WindowMenu,
    };

    private static QuillFrameRenderer Glass() => new(Theme(QuillFrameTheme.DarkGlass()));

    private static QuillFrameTheme Theme(QuillFrameTheme theme)
    {
        theme.Face = QuillFrameFonts.Bundled();
        return theme;
    }

    [Fact]
    public void Glass_keeps_the_layout_and_the_hit_test_of_the_plain_frame()
    {
        var glass = Glass();
        var plain = new QuillFrameRenderer(Theme(new QuillFrameTheme { Border = 6, CornerRadius = 12f }));
        Assert.Equal(plain.Measure(State, 1.0), glass.Measure(State, 1.0));
        LayoutOnly(glass, State, default);
        LayoutOnly(plain, State, default);

        foreach (var part in new[] { FramePart.Title, FramePart.Menu, FramePart.Close, FramePart.Maximize, FramePart.Minimize })
        {
            Assert.Equal(plain.Geometry.BoundsOf(part), glass.Geometry.BoundsOf(part));
        }

        for (var y = 0; y < glass.Geometry.Height; y += 3)
        {
            for (var x = 0; x < glass.Geometry.Width; x += 3)
            {
                Assert.Equal(plain.PartAt(x, y, State, 1.0), glass.PartAt(x, y, State, 1.0));
            }
        }
    }

    [Fact]
    public void Glass_publishes_a_shape_and_no_region()
    {
        var glass = Glass();
        using var region = new PixmanRegion32();
        Assert.False(glass.BackdropShape(State, 1.0, out _));

        LayoutOnly(glass, State, default);
        Assert.False(glass.BackdropRegion(State, 1.0, region));
        Assert.False(glass.OpaqueChrome);
        Assert.True(glass.BackdropShape(State, 1.0, out var shape));

        var geometry = glass.Geometry;
        Assert.Equal(geometry.Width, shape.Width);
        Assert.Equal(geometry.Height, shape.Height);
        Assert.Equal(12, shape.CornerRadius);
        Assert.Equal(10, shape.EdgeWidth);
        Assert.Equal(new Box(6, 36, 240, 100), shape.ClientHole);
        Assert.Equal(6, shape.ClientRadius);
        Assert.Equal(1, shape.Emphasis);
        Assert.Equal(0, shape.HighlightStrength);

        var custom = QuillFrameTheme.DarkGlass();
        custom.Face = QuillFrameFonts.Bundled();
        custom.Border = 10;
        custom.ContentRadius = 3;
        var inset = new QuillFrameRenderer(custom);
        LayoutOnly(inset, State, default);
        Assert.True(inset.BackdropShape(State, 1.0, out var insetShape));
        Assert.Equal(new Box(10, 40, 240, 100), insetShape.ClientHole);
        Assert.Equal(3, insetShape.ClientRadius);
        custom.ContentRadius = -1;
        custom.CornerRadius = 8;
        Assert.Equal(0, custom.ResolvedContentRadius);

        LayoutOnly(glass, State with { Shaded = true }, default);
        Assert.True(glass.BackdropShape(State, 1.0, out var shaded));
        Assert.True(shaded.ClientHole.IsEmpty);
    }

    [Fact]
    public void A_plain_quill_frame_publishes_no_shape_and_never_animates()
    {
        var plain = new QuillFrameRenderer(Theme(new QuillFrameTheme()));
        LayoutOnly(plain, State, default);
        Assert.False(plain.BackdropShape(State, 1.0, out _));
        var hovering = new FrameInteraction(FramePart.Close, FramePart.None) { HoverChangedAt = 10 * Ms, Now = 11 * Ms };
        Assert.False(plain.Animating(State, hovering));
    }

    [Fact]
    public void Glass_rests_until_something_eases()
    {
        var glass = Glass();
        var rest = new FrameInteraction(FramePart.None, FramePart.None) { Now = 1000 * Ms };
        LayoutOnly(glass, State, rest);
        Assert.False(glass.Animating(State, rest));

        var hover = rest with { Hot = FramePart.Close, HoverChangedAt = 1000 * Ms };
        Assert.True(glass.Animating(State, hover with { Now = 1100 * Ms }));
        Assert.False(glass.Animating(State, hover with { Now = 1151 * Ms }));

        var focus = rest with { ActiveChangedAt = 2000 * Ms };
        LayoutOnly(glass, State with { Active = false }, focus with { Now = 2000 * Ms });
        Assert.True(glass.BackdropShape(State, 1.0, out var start));
        Assert.Equal(1, start.Emphasis);
        LayoutOnly(glass, State with { Active = false }, focus with { Now = 2075 * Ms });
        Assert.True(glass.BackdropShape(State, 1.0, out var middle));
        Assert.Equal(0.5, middle.Emphasis, 6);
        Assert.True(glass.Animating(State with { Active = false }, focus with { Now = 2075 * Ms }));
        LayoutOnly(glass, State with { Active = false }, focus with { Now = 2200 * Ms });
        Assert.True(glass.BackdropShape(State, 1.0, out var end));
        Assert.Equal(0, end.Emphasis);
        Assert.False(glass.Animating(State with { Active = false }, focus with { Now = 2200 * Ms }));
    }

    [Fact]
    public void The_glint_eases_toward_the_pointer_and_fades_on_leave()
    {
        var glass = Glass();
        var now = 1000 * Ms;
        var inside = new FrameInteraction(FramePart.Title, FramePart.None)
        {
            PointerX = 60,
            PointerY = 15,
            PointerInside = true,
            PointerChangedAt = now,
            Now = now,
        };
        LayoutOnly(glass, State, inside);
        Assert.True(glass.BackdropShape(State, 1.0, out var entered));
        Assert.Equal((60.0, 15.0), (entered.HighlightX, entered.HighlightY));
        Assert.Equal(0, entered.HighlightStrength);
        Assert.True(glass.Animating(State, inside));

        for (var t = 1; t <= 30; t++)
        {
            LayoutOnly(glass, State, inside with { Now = now + (t * 16 * Ms) });
        }

        Assert.True(glass.BackdropShape(State, 1.0, out var lit));
        Assert.Equal(1, lit.HighlightStrength);
        Assert.False(glass.Animating(State, inside with { Now = now + (480 * Ms) }));

        now += 1000 * Ms;
        var moved = inside with { PointerX = 160, PointerChangedAt = now, Now = now };
        Assert.True(glass.Animating(State, moved));
        LayoutOnly(glass, State, moved);
        LayoutOnly(glass, State, moved with { Now = now + (16 * Ms) });
        Assert.True(glass.BackdropShape(State, 1.0, out var easing));
        Assert.InRange(easing.HighlightX, 61, 159);
        for (var t = 2; t <= 40; t++)
        {
            LayoutOnly(glass, State, moved with { Now = now + (t * 16 * Ms) });
        }

        Assert.True(glass.BackdropShape(State, 1.0, out var arrived));
        Assert.Equal(160, arrived.HighlightX);
        Assert.False(glass.Animating(State, moved with { Now = now + (640 * Ms) }));

        now += 1000 * Ms;
        var left = moved with { Hot = FramePart.None, PointerInside = false, PointerChangedAt = now, Now = now };
        Assert.True(glass.Animating(State, left));
        for (var t = 0; t <= 40; t++)
        {
            LayoutOnly(glass, State, left with { Now = now + (t * 16 * Ms) });
        }

        Assert.True(glass.BackdropShape(State, 1.0, out var gone));
        Assert.Equal(0, gone.HighlightStrength);
        Assert.False(glass.Animating(State, left with { Now = now + (640 * Ms) }));
    }

    [Fact]
    public void The_glint_moves_while_the_pointer_keeps_moving()
    {
        var glass = Glass();
        var now = 1000 * Ms;
        var pointer = new FrameInteraction(FramePart.Title, FramePart.None)
        {
            PointerX = 60,
            PointerY = 15,
            PointerInside = true,
            PointerChangedAt = now,
            Now = now,
        };
        for (var t = 0; t <= 30; t++)
        {
            LayoutOnly(glass, State, pointer with { Now = now + (t * 16 * Ms) });
        }

        now += 1000 * Ms;
        for (var t = 0; t < 10; t++)
        {
            var at = now + (t * 16 * Ms);
            LayoutOnly(glass, State, pointer with { PointerX = 60 + (10 * (t + 1)), PointerChangedAt = at - (2 * Ms), Now = at });
        }

        Assert.True(glass.BackdropShape(State, 1.0, out var shape));
        Assert.InRange(shape.HighlightX, 120, 159);
    }

    [Fact]
    public void A_glass_button_damages_its_glow()
    {
        var glass = Glass();
        LayoutOnly(glass, State, default);
        var close = glass.Geometry.Close;
        var bounds = glass.PartBounds(FramePart.Close);
        Assert.True(bounds.X <= close.X - 4 && bounds.Right >= close.Right + 4);
        Assert.Equal(glass.Geometry.Title, glass.PartBounds(FramePart.Title));
    }

    [Theory]
    [InlineData("gl", "gl")]
    [InlineData("vulkan", "vulkan")]
    public void A_redraw_swaps_to_a_new_buffer_instead_of_drawing_into_the_shown_one(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;
        var frame = new Basin.Scene.Frame(lease!.Host, Glass(), host.Scene.Root) { Clock = clock, Time = () => now };
        frame.Configure(new Box(40, 60, 240, 100), 1.0, State);
        frame.Commit();
        var shown = frame.StripNodes[0].Buffer;
        Assert.NotNull(shown);
        Assert.True(shown!.TryGetDmabuf(out _));

        var seen = new HashSet<IBuffer> { shown };
        frame.PointerMotion(40 + 120, 60 - 20);
        Assert.True(frame.Animating);
        Assert.False(ReferenceEquals(shown, frame.StripNodes[0].Buffer), "the hover redraw drew into the shown buffer");
        shown = frame.StripNodes[0].Buffer;
        for (var i = 1; i <= 6; i++)
        {
            clock.BeginFrame(host.Output, now + (i * 16_000_000));
            var redrawn = frame.StripNodes[0].Buffer;
            Assert.NotNull(redrawn);
            Assert.False(ReferenceEquals(shown, redrawn), $"tick {i} drew into the shown buffer");
            Assert.Same(redrawn, frame.PresentedChrome);
            foreach (var strip in frame.StripNodes)
            {
                Assert.True(!strip.Enabled || ReferenceEquals(strip.Buffer, redrawn));
            }

            shown = redrawn;
            seen.Add(redrawn!);
        }

        Assert.InRange(seen.Count, 2, 3);
        frame.Dispose();
    }

    [Theory]
    [InlineData("gl", "gl", "quill-glass-frame-gl")]
    [InlineData("vulkan", "vulkan", "quill-glass-frame-vk")]
    public void Golden_glass_chrome(string row, string backend, string golden)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");

        var renderer = Glass();
        var client = new Box(0, 0, 240, 100);
        var insets = renderer.Measure(State, 1.0);
        var surface = (IQuillUISurface)lease!.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = client.Width + insets.Left + insets.Right,
            Height = client.Height + insets.Top + insets.Bottom,
            Scale = 1.0,
        })!;

        var hot = new FrameInteraction(FramePart.Close, FramePart.None) { HoverChangedAt = 1, Now = 1000 * Ms };
        renderer.Draw(surface, new Box(insets.Left, insets.Top, client.Width, client.Height), State, hot);
        Assert.True(surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, host.Renderer, out var rgba));
        var center = (((buffer.Height / 2) * buffer.Width) + (buffer.Width / 2)) * 4;
        Assert.Equal(0, rgba![center + 3]);
        Golden.AssertMatches(rgba, buffer.Width, buffer.Height, golden, tolerance: 6);

        frame.Dispose();
        surface.Dispose();
    }

    private static void LayoutOnly(QuillFrameRenderer renderer, in FrameState state, in FrameInteraction interaction)
    {
        var insets = renderer.Measure(state, 1.0);
        var surface = new LayoutOnlySurface(240 + insets.Left + insets.Right, 100 + insets.Top + insets.Bottom);
        try
        {
            renderer.Draw(surface, new Box(insets.Left, insets.Top, 240, 100), state, interaction);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
