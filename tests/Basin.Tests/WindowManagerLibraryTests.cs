using Basin.Config;
using Basin.WindowManager;
using Xunit;

namespace Basin.Tests;

public class WindowManagerLibraryTests
{
    [Fact]
    public void Opaque_white_scales_to_the_full_32_bit_range()
    {
        var white = WmColor.FromRgba(0xff, 0xff, 0xff);

        Assert.Equal(0xffffffffu, white.R);
        Assert.Equal(0xffffffffu, white.G);
        Assert.Equal(0xffffffffu, white.B);
        Assert.Equal(0xffffffffu, white.A);
    }

    [Fact]
    public void Channels_are_premultiplied_by_alpha()
    {
        var half = WmColor.FromRgba(0xff, 0x00, 0x00, 0x80);

        Assert.Equal(0x80u * 0x01010101u, half.A);
        Assert.Equal(half.A, half.R);
        Assert.Equal(0u, half.G);
        Assert.Equal(0u, half.B);
    }

    [Fact]
    public void Packed_literal_matches_the_channel_constructor()
    {
        Assert.Equal(WmColor.FromRgba(0x7a, 0xa2, 0xf7, 0xff), WmColor.FromRgba(0x7aa2f7ffu));
    }

    [Fact]
    public void Fully_transparent_premultiplies_every_channel_away()
    {
        var clear = WmColor.FromRgba(0xff, 0xff, 0xff, 0x00);

        Assert.Equal(0u, clear.A);
        Assert.Equal(0u, clear.R);
    }

    [Fact]
    public void Rectangles_that_do_not_touch_intersect_to_nothing()
    {
        var left = new Rect(0, 0, 10, 10);
        var right = new Rect(20, 0, 10, 10);

        Assert.True(left.Intersect(right).IsEmpty);
    }

    [Fact]
    public void Overlapping_rectangles_intersect_to_the_shared_area()
    {
        var a = new Rect(0, 0, 100, 50);
        var b = new Rect(60, 10, 100, 100);

        Assert.Equal(new Rect(60, 10, 40, 40), a.Intersect(b));
    }

    [Fact]
    public void A_rectangle_contains_its_top_left_but_not_its_bottom_right()
    {
        var box = new Rect(5, 5, 10, 10);

        Assert.True(box.Contains(new WindowManager.Point(5, 5)));
        Assert.False(box.Contains(new WindowManager.Point(15, 15)));
        Assert.True(box.Contains(new WindowManager.Point(14, 14)));
    }

    [Fact]
    public void Size_hints_clamp_only_the_dimensions_the_window_expressed()
    {
        var hint = new DimensionsHint(new Size(100, 0), new Size(400, 0));

        Assert.Equal(new Size(400, 9999), hint.Clamp(new Size(800, 9999)));
        Assert.Equal(new Size(100, 1), hint.Clamp(new Size(10, 1)));
    }

    [Fact]
    public void Latency_percentiles_are_zero_before_anything_is_recorded()
    {
        var latency = new WmLatency();

        Assert.Equal(0, latency.Sequences);
        Assert.Equal(TimeSpan.Zero, latency.Median);
        Assert.Equal(TimeSpan.Zero, latency.P99);
    }

    [Fact]
    public void Latency_median_and_worst_track_the_recorded_samples()
    {
        var latency = new WmLatency();
        for (var i = 1; i <= 100; i++)
        {
            latency.Record(i * System.Diagnostics.Stopwatch.Frequency / 1000);
        }

        Assert.Equal(100, latency.Sequences);
        Assert.InRange(latency.Median.TotalMilliseconds, 49.0, 52.0);
        Assert.InRange(latency.P99.TotalMilliseconds, 98.0, 100.5);
        Assert.InRange(latency.Worst.TotalMilliseconds, 99.0, 100.5);
    }

    [Fact]
    public void Latency_keeps_the_worst_ever_after_the_ring_wraps()
    {
        var latency = new WmLatency();
        var tick = System.Diagnostics.Stopwatch.Frequency / 1000;
        latency.Record(500 * tick);
        for (var i = 0; i < 2000; i++)
        {
            latency.Record(tick);
        }

        Assert.Equal(2001, latency.Sequences);
        Assert.InRange(latency.Worst.TotalMilliseconds, 499.0, 501.0);
        Assert.InRange(latency.Median.TotalMilliseconds, 0.5, 1.5);
    }

    [Fact]
    public void Keysym_names_resolve_and_unknown_ones_report_the_name()
    {
        Assert.NotEqual(Keysym.NoSymbol, Keysym.FromName("Return"));
        Assert.NotEqual(Keysym.NoSymbol, Keysym.FromName("q"));
        Assert.Equal(Keysym.NoSymbol, Keysym.FromName("NotAKeysymAtAll"));

        var error = Assert.Throws<ArgumentException>(() => Keysym.Require("NotAKeysymAtAll"));
        Assert.Contains("NotAKeysymAtAll", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Edges_and_modifiers_match_the_protocol_bit_values()
    {
        Assert.Equal(1, (int)Edges.Top);
        Assert.Equal(2, (int)Edges.Bottom);
        Assert.Equal(4, (int)Edges.Left);
        Assert.Equal(8, (int)Edges.Right);

        Assert.Equal(1, (int)Modifiers.Shift);
        Assert.Equal(4, (int)Modifiers.Ctrl);
        Assert.Equal(8, (int)Modifiers.Mod1);
        Assert.Equal(32, (int)Modifiers.Mod3);
        Assert.Equal(64, (int)Modifiers.Mod4);
        Assert.Equal(128, (int)Modifiers.Mod5);

        Assert.DoesNotContain(Enum.GetValues<Modifiers>(), m => (int)m is 2 or 16);
    }

    [Fact]
    public void Window_capabilities_match_the_protocol_bit_values()
    {
        Assert.Equal(1, (int)WindowCapabilities.WindowMenu);
        Assert.Equal(2, (int)WindowCapabilities.Maximize);
        Assert.Equal(4, (int)WindowCapabilities.Fullscreen);
        Assert.Equal(8, (int)WindowCapabilities.Minimize);
    }
}
