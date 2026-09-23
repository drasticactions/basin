using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class RoundedRegionTests
{
    [Theory]
    [InlineData(240, 30, 8f)]
    [InlineData(240, 30, 8.5f)]
    [InlineData(7, 7, 4f)]
    [InlineData(5, 40, 3.6f)]
    [InlineData(300, 5, 2.5f)]
    [InlineData(100, 100, 0f)]
    [InlineData(100, 100, -3f)]
    [InlineData(1, 1, 12f)]
    [InlineData(64, 17, 100f)]
    public void The_circle_overload_is_quill_rect_for_rect(int width, int height, float cornerRadius)
    {
        using var expected = new PixmanRegion32();
        using var actual = new PixmanRegion32();
        var expectedFilled = QuillBefore(expected, width, height, cornerRadius);
        var actualFilled = RoundedRegion.Fill(actual, width, height, (int)Math.Round(Math.Max(0f, cornerRadius)));

        Assert.Equal(expectedFilled, actualFilled);
        Assert.Equal(expected.Rectangles(), actual.Rectangles());
    }

    [Fact]
    public void The_circle_overload_matches_quill_over_a_sweep()
    {
        using var expected = new PixmanRegion32();
        using var actual = new PixmanRegion32();
        for (var width = 1; width < 40; width += 3)
        {
            for (var height = 1; height < 40; height += 2)
            {
                for (var radius = 0f; radius < 24f; radius += 0.75f)
                {
                    QuillBefore(expected, width, height, radius);
                    RoundedRegion.Fill(actual, width, height, (int)Math.Round(radius));
                    Assert.Equal(expected.Rectangles(), actual.Rectangles());
                }
            }
        }
    }

    [Fact]
    public void The_marco_inset_is_the_row_width_clear_corners_used()
    {
        for (var corner = 1; corner < 40; corner++)
        {
            var r = Math.Sqrt(corner) + corner;
            for (var i = 0; i < corner; i++)
            {
                var before = (int)Math.Floor(0.5 + r - Math.Sqrt(r * r - (r - (i + 0.5)) * (r - (i + 0.5))));
                Assert.Equal(before, RoundedRegion.MarcoInset(corner, i));
            }
        }
    }

    [Fact]
    public void Top_corners_leave_the_bottom_square()
    {
        using var region = new PixmanRegion32();
        Assert.True(RoundedRegion.Fill(region, 50, 20, 6, RoundedCorners.Top));

        Assert.False(region.Contains(0, 0));
        Assert.True(region.Contains(0, 19));
        Assert.True(region.Contains(49, 19));
        Assert.True(region.Contains(25, 0));
    }

    [Fact]
    public void Row_insets_round_only_the_named_corners()
    {
        using var region = new PixmanRegion32();
        ReadOnlySpan<int> insets = [3, 1];
        Assert.True(RoundedRegion.Fill(region, 10, 10, insets, RoundedCorners.TopLeft | RoundedCorners.BottomRight));

        Assert.False(region.Contains(2, 0));
        Assert.True(region.Contains(3, 0));
        Assert.True(region.Contains(9, 0));
        Assert.False(region.Contains(0, 1));
        Assert.True(region.Contains(0, 9));
        Assert.False(region.Contains(7, 9));
        Assert.True(region.Contains(6, 9));
    }

    [Fact]
    public void Four_curves_round_each_corner_by_its_own_rows()
    {
        using var four = new PixmanRegion32();
        using var one = new PixmanRegion32();
        ReadOnlySpan<int> curve = [4, 2, 1, 1];
        Assert.True(RoundedRegion.Fill(four, 30, 20, curve, curve, curve, curve));
        RoundedRegion.Fill(one, 30, 20, curve);
        Assert.Equal(one.Rectangles(), four.Rectangles());

        ReadOnlySpan<int> small = [1];
        Assert.True(RoundedRegion.Fill(four, 30, 20, curve, small, [], []));
        Assert.False(four.Contains(3, 0));
        Assert.True(four.Contains(4, 0));
        Assert.False(four.Contains(29, 0));
        Assert.True(four.Contains(28, 0));
        Assert.True(four.Contains(29, 1));
        Assert.True(four.Contains(0, 19));
        Assert.True(four.Contains(29, 19));
    }

    [Fact]
    public void An_empty_size_clears_and_reports_empty()
    {
        using var region = new PixmanRegion32();
        region.UnionRect(region, 0, 0, 5, 5);

        Assert.False(RoundedRegion.Fill(region, 0, 10, 4));
        Assert.True(region.IsEmpty);
    }

    private static bool QuillBefore(PixmanRegion32 into, int width, int height, float cornerRadius)
    {
        into.Clear();
        var radius = (int)Math.Round(Math.Min(cornerRadius, Math.Min(width, height) / 2.0));
        if (radius <= 0)
        {
            into.UnionRect(into, 0, 0, (uint)width, (uint)height);
            return true;
        }

        for (var y = 0; y < radius; y++)
        {
            var dy = radius - y - 0.5;
            var inset = (int)Math.Round(radius - Math.Sqrt((radius * radius) - (dy * dy)));
            Row(into, width, y, inset);
            Row(into, width, height - 1 - y, inset);
        }

        var straight = height - (2 * radius);
        if (straight > 0)
        {
            into.UnionRect(into, 0, radius, (uint)width, (uint)straight);
        }

        return !into.IsEmpty;
    }

    private static void Row(PixmanRegion32 into, int width, int y, int inset)
    {
        var run = width - (2 * inset);
        if (run > 0 && y >= 0)
        {
            into.UnionRect(into, inset, y, (uint)run, 1);
        }
    }
}
