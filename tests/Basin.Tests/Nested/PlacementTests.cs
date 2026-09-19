using Basin.Shell.Nested;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class WindowPlacementTests
{
    private static readonly Box Screen = new(0, 0, 1920, 1080);

    private static PlacementRequest Request(
        int width,
        int height,
        Box[]? visible = null,
        PlacementMode mode = PlacementMode.Automatic,
        bool center = false,
        Point pointer = default,
        Box? parent = null,
        int parentTitle = 24,
        Box? workArea = null,
        Box? output = null) =>
        new(width, height, workArea ?? Screen, visible ?? [], pointer, mode, center, parent, parentTitle, output ?? Screen);

    [Fact]
    public void First_fit_prefers_the_origin()
    {
        Assert.Equal(new Point(0, 0), Placement.Place(Request(800, 600)));
    }

    [Fact]
    public void First_fit_starts_at_the_work_area_origin()
    {
        var placed = Placement.Place(Request(800, 600, workArea: new Box(0, 24, 1920, 1032)));

        Assert.Equal(new Point(0, 24), placed);
    }

    [Fact]
    public void First_fit_goes_right_of_a_window_when_below_does_not_fit()
    {
        var placed = Placement.Place(Request(800, 600, [new Box(0, 0, 800, 600)]));

        Assert.Equal(new Point(800, 0), placed);
    }

    [Fact]
    public void First_fit_goes_below_a_window_when_that_is_nearer_the_origin()
    {
        var placed = Placement.Place(Request(800, 400, [new Box(0, 0, 800, 400)]));

        Assert.Equal(new Point(0, 400), placed);
    }

    [Fact]
    public void First_fit_tries_candidates_north_west_first_whatever_the_list_order()
    {
        Box[] visible = [new Box(1000, 0, 800, 600), new Box(0, 0, 800, 600)];

        var placed = Placement.Place(Request(200, 200, visible));

        Assert.Equal(new Point(0, 600), placed);
    }

    [Fact]
    public void First_fit_skips_a_candidate_that_overlaps_another_window()
    {
        Box[] visible = [new Box(0, 0, 800, 600), new Box(800, 0, 400, 300)];

        var placed = Placement.Place(Request(800, 500, visible));

        Assert.Equal(new Point(800, 300), placed);
    }

    [Fact]
    public void Cascade_steps_from_the_window_at_the_origin()
    {
        var placed = Placement.Place(Request(1000, 700, [new Box(0, 0, 1000, 700)]));

        Assert.Equal(new Point(25, 25), placed);
    }

    [Fact]
    public void Cascade_follows_the_diagonal_whatever_the_list_order()
    {
        Box[] visible = [new Box(25, 25, 1000, 700), new Box(0, 0, 1000, 700)];

        var placed = Placement.Place(Request(1000, 700, visible));

        Assert.Equal(new Point(50, 50), placed);
    }

    [Fact]
    public void Cascade_ignores_a_window_off_the_diagonal()
    {
        Box[] visible = [new Box(0, 0, 1000, 700), new Box(300, 25, 1000, 700)];

        var placed = Placement.Place(Request(1000, 700, visible));

        Assert.Equal(new Point(25, 25), placed);
    }

    [Fact]
    public void Cascade_starts_a_new_column_when_it_runs_off_the_work_area()
    {
        Box[] visible = [new Box(0, 0, 150, 150), new Box(25, 25, 150, 150), new Box(50, 50, 150, 150)];

        var placed = Placement.Place(Request(150, 150, visible, workArea: new Box(0, 0, 300, 200)));

        Assert.Equal(new Point(50, 0), placed);
    }

    [Fact]
    public void Cascade_returns_to_the_origin_when_no_column_is_left()
    {
        Box[] visible = [new Box(0, 0, 150, 150), new Box(25, 25, 150, 150), new Box(50, 50, 150, 150)];

        var placed = Placement.Place(Request(150, 150, visible, workArea: new Box(0, 0, 200, 200)));

        Assert.Equal(new Point(0, 0), placed);
    }

    [Fact]
    public void Center_new_windows_centers_a_window_that_fits_nowhere()
    {
        var placed = Placement.Place(Request(1000, 700, [new Box(0, 0, 1000, 700)], center: true));

        Assert.Equal(new Point(460, 190), placed);
    }

    [Fact]
    public void Center_new_windows_still_places_a_window_that_fits_by_first_fit()
    {
        var placed = Placement.Place(Request(800, 600, [new Box(0, 0, 800, 600)], center: true));

        Assert.Equal(new Point(800, 0), placed);
    }

    [Fact]
    public void A_centered_window_larger_than_the_work_area_keeps_its_top_left_inside()
    {
        var placed = Placement.Place(Request(2000, 1200, center: true, workArea: new Box(0, 24, 1920, 1032)));

        Assert.Equal(new Point(0, 24), placed);
    }

    [Fact]
    public void A_transient_is_centered_over_its_parent_below_the_title()
    {
        var placed = Placement.Place(Request(400, 300, parent: new Box(100, 100, 800, 600)));

        Assert.Equal(new Point(300, 216), placed);
    }

    [Fact]
    public void A_transient_is_clamped_to_the_work_area()
    {
        var placed = Placement.Place(Request(400, 300, parent: new Box(1700, 900, 800, 600)));

        Assert.Equal(new Point(1520, 780), placed);
    }

    [Fact]
    public void A_transient_ignores_the_placement_mode()
    {
        var placed = Placement.Place(Request(400, 300, mode: PlacementMode.Pointer, pointer: new Point(10, 10), parent: new Box(100, 100, 800, 600)));

        Assert.Equal(new Point(300, 216), placed);
    }

    [Theory]
    [InlineData(PlacementMode.Pointer, 500, 400, 100, 100)]
    [InlineData(PlacementMode.Manual, 500, 400, 100, 100)]
    [InlineData(PlacementMode.Pointer, 10, 10, 0, 0)]
    [InlineData(PlacementMode.Pointer, 1915, 1075, 1120, 480)]
    [InlineData(PlacementMode.Manual, 1915, 10, 1120, 0)]
    public void Pointer_placement_centers_the_frame_under_the_pointer_and_clamps_to_the_output(PlacementMode mode, int px, int py, int x, int y)
    {
        var placed = Placement.Place(Request(800, 600, [new Box(0, 0, 1920, 1080)], mode, pointer: new Point(px, py)));

        Assert.Equal(new Point(x, y), placed);
    }

    [Fact]
    public void Pointer_placement_clamps_to_the_output_rather_than_the_work_area()
    {
        var placed = Placement.Place(Request(800, 600, mode: PlacementMode.Pointer, pointer: new Point(10, 10), workArea: new Box(0, 24, 1920, 1032)));

        Assert.Equal(new Point(0, 0), placed);
    }

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(2000, 2000, true)]
    [InlineData(2000, 500, false)]
    [InlineData(1920, 1079, false)]
    [InlineData(800, 600, false)]
    public void Automaximize_needs_the_frame_to_cover_the_work_area_in_both_dimensions(int width, int height, bool expected)
    {
        Assert.Equal(expected, Placement.ShouldMaximize(width, height, Screen));
    }
}
