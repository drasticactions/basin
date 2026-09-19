using Basin.Shell.Xdg;
using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class ConstraintsTests
{
    private static readonly Box WorkArea = new(0, 0, 1920, 1080);

    private const int Title = 24;

    [Fact]
    public void A_frame_inside_the_work_area_is_left_alone()
    {
        var frame = new Box(100, 100, 800, 600);

        Assert.Equal(frame, Constraints.KeepTitleOnScreen(frame, WorkArea, Title));
        Assert.Equal(frame, Constraints.PartiallyOnScreen(frame, WorkArea, Title));
        Assert.Equal(frame, Constraints.FitWorkArea(frame, WorkArea));
    }

    [Fact]
    public void The_title_never_goes_above_the_work_area()
    {
        var kept = Constraints.KeepTitleOnScreen(new Box(100, -50, 800, 600), WorkArea, Title);

        Assert.Equal(new Box(100, 0, 800, 600), kept);
    }

    [Fact]
    public void The_title_never_goes_above_a_top_panel()
    {
        var kept = Constraints.KeepTitleOnScreen(new Box(100, 0, 800, 600), new Box(0, 24, 1920, 1032), Title);

        Assert.Equal(new Box(100, 24, 800, 600), kept);
    }

    [Fact]
    public void The_title_may_touch_the_bottom_of_the_work_area_but_not_pass_it()
    {
        var kept = Constraints.KeepTitleOnScreen(new Box(100, 1070, 800, 600), WorkArea, Title);

        Assert.Equal(new Box(100, 1056, 800, 600), kept);
    }

    [Theory]
    [InlineData(1900, 1845)]
    [InlineData(-800, -725)]
    [InlineData(-700, -700)]
    public void At_least_seventy_five_pixels_stay_visible_horizontally(int x, int expected)
    {
        var kept = Constraints.KeepTitleOnScreen(new Box(x, 100, 800, 600), WorkArea, Title);

        Assert.Equal(expected, kept.X);
    }

    [Fact]
    public void A_tiny_frame_keeps_ten_pixels_visible()
    {
        var kept = Constraints.KeepTitleOnScreen(new Box(-15, 100, 20, 20), WorkArea, Title);

        Assert.Equal(-10, kept.X);
    }

    [Fact]
    public void A_frame_keeps_a_quarter_of_its_width_when_that_is_under_seventy_five()
    {
        var kept = Constraints.KeepTitleOnScreen(new Box(-1000, 100, 200, 100), WorkArea, Title);

        Assert.Equal(-150, kept.X);
    }

    [Fact]
    public void Partially_on_screen_lets_the_frame_go_above_the_work_area()
    {
        var kept = Constraints.PartiallyOnScreen(new Box(100, -500, 800, 600), WorkArea, Title);

        Assert.Equal(new Box(100, -500, 800, 600), kept);
    }

    [Fact]
    public void Partially_on_screen_keeps_seventy_five_pixels_of_a_tall_frame_below_the_top()
    {
        var kept = Constraints.PartiallyOnScreen(new Box(100, -600, 800, 600), WorkArea, Title);

        Assert.Equal(-525, kept.Y);
    }

    [Fact]
    public void Partially_on_screen_keeps_the_title_above_the_bottom()
    {
        var kept = Constraints.PartiallyOnScreen(new Box(100, 1070, 800, 600), WorkArea, Title);

        Assert.Equal(1056, kept.Y);
    }

    [Fact]
    public void Partially_on_screen_shares_the_horizontal_rule()
    {
        var kept = Constraints.PartiallyOnScreen(new Box(1900, 100, 800, 600), WorkArea, Title);

        Assert.Equal(1845, kept.X);
    }

    [Fact]
    public void A_top_edge_resize_stops_at_the_top_of_the_work_area()
    {
        var clipped = Constraints.ClipResize(new Box(100, -40, 800, 640), ResizeEdges.Top, new Box(0, 24, 1920, 1032), 32, 32);

        Assert.Equal(new Box(100, 24, 800, 576), clipped);
    }

    [Fact]
    public void A_top_edge_resize_keeps_the_minimum_height()
    {
        var clipped = Constraints.ClipResize(new Box(100, -40, 800, 100), ResizeEdges.Top, new Box(0, 24, 1920, 1032), 32, 80);

        Assert.Equal(new Box(100, -20, 800, 80), clipped);
    }

    [Fact]
    public void A_bottom_edge_resize_stops_at_the_bottom_of_the_work_area()
    {
        var clipped = Constraints.ClipResize(new Box(100, 100, 800, 1200), ResizeEdges.Bottom, new Box(0, 24, 1920, 1032), 32, 32);

        Assert.Equal(new Box(100, 100, 800, 956), clipped);
    }

    [Fact]
    public void A_side_resize_stops_at_the_allowed_overhang()
    {
        var left = Constraints.ClipResize(new Box(-800, 100, 800, 600), ResizeEdges.Left, WorkArea, 32, 32);
        var right = Constraints.ClipResize(new Box(2000, 100, 800, 600), ResizeEdges.Right, WorkArea, 32, 32);

        Assert.Equal(new Box(-725, 100, 725, 600), left);
        Assert.Equal(new Box(2000, 100, 645, 600), right);
    }

    [Fact]
    public void Fit_work_area_shrinks_a_frame_that_is_too_large()
    {
        var fitted = Constraints.FitWorkArea(new Box(0, 0, 2000, 1200), WorkArea);

        Assert.Equal(WorkArea, fitted);
    }

    [Fact]
    public void Fit_work_area_moves_a_frame_back_inside()
    {
        var fitted = Constraints.FitWorkArea(new Box(1500, 900, 800, 600), WorkArea);

        Assert.Equal(new Box(1120, 480, 800, 600), fitted);
    }

    [Fact]
    public void Fit_work_area_moves_below_a_top_panel()
    {
        var fitted = Constraints.FitWorkArea(new Box(0, 0, 1920, 1080), new Box(0, 24, 1920, 1032));

        Assert.Equal(new Box(0, 24, 1920, 1032), fitted);
    }

    [Fact]
    public void Fit_work_area_moves_a_frame_left_and_up_of_the_work_area_to_its_origin()
    {
        var fitted = Constraints.FitWorkArea(new Box(-300, -200, 800, 600), WorkArea);

        Assert.Equal(new Box(0, 0, 800, 600), fitted);
    }

    [Theory]
    [InlineData(800, 600, 100, 100, 0, 0, 800, 600)]
    [InlineData(50, 50, 100, 100, 0, 0, 100, 100)]
    [InlineData(800, 600, 100, 100, 500, 400, 500, 400)]
    [InlineData(5000, 5000, 0, 0, 0, 0, 5000, 5000)]
    [InlineData(50, 50, 100, 100, 80, 80, 100, 100)]
    [InlineData(300, 300, 0, 0, 200, 0, 200, 300)]
    public void Clamp_size_honours_minimum_first_and_treats_zero_maximum_as_unbounded(int width, int height, int minWidth, int minHeight, int maxWidth, int maxHeight, int expectedWidth, int expectedHeight)
    {
        var clamped = Constraints.ClampSize(width, height, minWidth, minHeight, maxWidth, maxHeight);

        Assert.Equal((expectedWidth, expectedHeight), clamped);
    }
}
