using EightWm;
using Xunit;

namespace Basin.Tests;

public sealed class CrossSlideTests
{
    private static Tile Subject() => new() { Name = "a", Exec = "true" };

    private static CrossSlide Started(out Tile tile)
    {
        var slide = new CrossSlide();
        tile = Subject();
        slide.Begin(tile, 100);
        return slide;
    }

    [Fact]
    public void A_touch_that_has_not_moved_is_only_started()
    {
        var slide = Started(out var tile);

        Assert.Equal(CrossSlideStage.Started, slide.Stage);
        Assert.Same(tile, slide.Tile);
        Assert.True(slide.IsActive);
    }

    [Fact]
    public void Crossing_the_first_threshold_selects()
    {
        var slide = Started(out _);

        Assert.Equal(CrossSlideStage.Started, slide.Update(120));
        Assert.Equal(CrossSlideStage.Selected, slide.Update(145));
    }

    [Fact]
    public void Crossing_the_second_threshold_detaches()
    {
        var slide = Started(out _);

        Assert.Equal(CrossSlideStage.Selected, slide.Update(160));
        Assert.Equal(CrossSlideStage.Detached, slide.Update(220));
    }

    [Fact]
    public void Every_stage_is_reversible()
    {
        var slide = Started(out _);

        slide.Update(230);
        Assert.Equal(CrossSlideStage.Detached, slide.Stage);

        Assert.Equal(CrossSlideStage.Selected, slide.Update(160));
        Assert.Equal(CrossSlideStage.Started, slide.Update(110));
    }

    [Fact]
    public void The_gesture_works_in_both_directions()
    {
        var slide = Started(out _);

        Assert.Equal(CrossSlideStage.Selected, slide.Update(40));
        Assert.Equal(CrossSlideStage.Detached, slide.Update(-40));
    }

    [Fact]
    public void Releasing_reports_the_stage_it_reached_and_forgets_the_tile()
    {
        var slide = Started(out _);
        slide.Update(160);

        Assert.Equal(CrossSlideStage.Selected, slide.Release());
        Assert.Equal(CrossSlideStage.None, slide.Stage);
        Assert.Null(slide.Tile);
        Assert.False(slide.IsActive);
    }

    [Fact]
    public void Releasing_short_of_the_first_threshold_does_nothing()
    {
        var slide = Started(out _);
        slide.Update(120);

        Assert.Equal(CrossSlideStage.Started, slide.Release());
    }

    [Fact]
    public void Travel_is_signed_and_measured_from_the_start()
    {
        var slide = Started(out _);

        slide.Update(160);
        Assert.Equal(60, slide.Travel);

        slide.Update(30);
        Assert.Equal(-70, slide.Travel);
    }

    [Fact]
    public void Aborting_drops_the_gesture_without_a_stage()
    {
        var slide = Started(out _);
        slide.Update(200);

        slide.Abort();

        Assert.Equal(CrossSlideStage.None, slide.Stage);
        Assert.Null(slide.Tile);
        Assert.Equal(CrossSlideStage.None, slide.Update(400));
    }

    [Fact]
    public void The_thresholds_are_configurable()
    {
        var slide = new CrossSlide { SelectThreshold = 10, DetachThreshold = 20 };
        slide.Begin(Subject(), 0);

        Assert.Equal(CrossSlideStage.Selected, slide.Update(12));
        Assert.Equal(CrossSlideStage.Detached, slide.Update(25));
    }

    [Fact]
    public void A_fresh_slide_carries_its_thresholds()
    {
        var slide = new CrossSlide();

        Assert.True(slide.SelectThreshold > 0);
        Assert.True(slide.DetachThreshold > slide.SelectThreshold);
    }

    [Fact]
    public void A_slide_with_no_thresholds_detaches_on_the_first_touch()
    {
        var slide = default(CrossSlide);
        slide.Begin(new Tile { Name = "a", Exec = "true" }, 100);

        Assert.Equal(CrossSlideStage.Detached, slide.Update(100));
    }
}
