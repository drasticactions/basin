using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class GlassFieldTests
{
    [Theory]
    [InlineData(50, 40, -40)]
    [InlineData(0, 40, 0)]
    [InlineData(-3, 40, 3)]
    [InlineData(50, 0, 0)]
    [InlineData(100, 40, 0)]
    [InlineData(12, 12, -12)]
    public void Distance_is_signed_and_flat_along_the_sides(double x, double y, double expected)
    {
        Assert.Equal(expected, GlassField.Distance(x, y, 100, 80, 12), 6);
    }

    [Fact]
    public void Distance_is_round_at_a_corner()
    {
        var inset = 12 - (12 / Math.Sqrt(2));
        Assert.Equal(0, GlassField.Distance(inset, inset, 100, 80, 12), 6);
        Assert.True(GlassField.Distance(0.5, 0.5, 100, 80, 12) > 0);
        Assert.Equal(12 * (Math.Sqrt(2) - 1), GlassField.Distance(0, 0, 100, 80, 12), 6);
    }

    [Fact]
    public void Radius_clamps_to_the_short_side()
    {
        Assert.Equal(GlassField.Distance(5, 5, 40, 10, 5), GlassField.Distance(5, 5, 40, 10, 50), 9);
    }

    [Theory]
    [InlineData(0.5, 40, -1, 0)]
    [InlineData(99.5, 40, 1, 0)]
    [InlineData(50, 0.5, 0, -1)]
    [InlineData(50, 79.5, 0, 1)]
    public void Normal_points_out_of_the_nearest_side(double x, double y, double nx, double ny)
    {
        var (gx, gy) = GlassField.Normal(x, y, 100, 80, 12);
        Assert.Equal(nx, gx, 6);
        Assert.Equal(ny, gy, 6);
    }

    [Fact]
    public void Normal_is_diagonal_in_a_corner()
    {
        var (gx, gy) = GlassField.Normal(2, 2, 100, 80, 12);
        Assert.Equal(-Math.Sqrt(0.5), gx, 6);
        Assert.Equal(-Math.Sqrt(0.5), gy, 6);
    }

    [Fact]
    public void Lens_pulls_inward_inside_the_bevel_only()
    {
        Assert.Equal((0.0, 0.0), GlassField.LensOffset(50, 40, 100, 80, 12, 10, 0.6));
        Assert.Equal((0.0, 0.0), GlassField.LensOffset(-1, 40, 100, 80, 12, 10, 0.6));

        var (edgeX, _) = GlassField.LensOffset(0.5, 40, 100, 80, 12, 10, 0.6);
        var (midX, _) = GlassField.LensOffset(5, 40, 100, 80, 12, 10, 0.6);
        Assert.True(edgeX > midX && midX > 0);
        Assert.Equal(0.6 * 10 * 0.95 * 0.95, edgeX, 6);

        var (_, bottomY) = GlassField.LensOffset(50, 79.5, 100, 80, 12, 10, 0.6);
        Assert.True(bottomY < 0);
    }

    [Fact]
    public void Lens_off_when_strength_or_bevel_is_zero()
    {
        Assert.Equal((0.0, 0.0), GlassField.LensOffset(0.5, 40, 100, 80, 12, 0, 0.6));
        Assert.Equal((0.0, 0.0), GlassField.LensOffset(0.5, 40, 100, 80, 12, 10, 0));
    }

    [Fact]
    public void Sample_never_leaves_the_clamp_box()
    {
        var clamp = new Box(0, 0, 100, 80);
        for (var y = 0.5; y < 80; y += 1)
        {
            for (var x = 0.5; x < 100; x += 1)
            {
                var (sx, sy) = GlassField.Sample(x, y, 100, 80, 12, 60, 4.0, clamp);
                Assert.InRange(sx, 0.5, 99.5);
                Assert.InRange(sy, 0.5, 79.5);
            }
        }

        var clipped = new Box(30, 0, 70, 80);
        var (cx, _) = GlassField.Sample(30.5, 40, 100, 80, 12, 10, 0.6, clipped);
        Assert.True(cx >= 30.5);
        var (rx, _) = GlassField.Sample(99.5, 40, 100, 80, 12, 10, 0.6, clipped);
        Assert.True(rx < 99.5);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(90, 0, 1)]
    [InlineData(270, 0, -1)]
    public void Light_angle_starts_at_the_right_in_screen_coordinates(double degrees, double x, double y)
    {
        var (lx, ly) = GlassField.TowardLight(degrees);
        Assert.Equal(x, lx, 9);
        Assert.Equal(y, ly, 9);
    }
}
