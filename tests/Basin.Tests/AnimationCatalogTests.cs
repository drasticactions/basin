using EightWm;
using Xunit;

namespace Basin.Tests;

public sealed class AnimationCatalogTests
{
    private static ref readonly AnimationSpec Spec(Animation name) => ref AnimationCatalog.Of(name);

    [Fact]
    public void Enter_page_slides_a_hundred_pixels_over_a_second_and_fades_in_over_170()
    {
        ref readonly var spec = ref Spec(Animation.EnterPage);

        Assert.Equal(MotionAxis.X, spec.Axis);
        Assert.Equal(100, spec.Offset.From);
        Assert.Equal(0, spec.Offset.To);
        Assert.Equal(1000u, spec.Offset.DurationMs);
        Assert.Equal(AnimationCurve.Deceleration, spec.Offset.Curve);
        Assert.Equal(170u, spec.Opacity.DurationMs);
        Assert.Equal(AnimationCurve.Linear, spec.Opacity.Curve);
        Assert.Equal(83u, spec.StaggerMs);
        Assert.Equal(333u, spec.StaggerCapMs);
    }

    [Fact]
    public void Exit_page_is_a_linear_fade_with_no_stagger()
    {
        ref readonly var spec = ref Spec(Animation.ExitPage);

        Assert.Equal(MotionAxis.None, spec.Axis);
        Assert.True(spec.Offset.IsEmpty);
        Assert.Equal(1, spec.Opacity.From);
        Assert.Equal(0, spec.Opacity.To);
        Assert.Equal(117u, spec.Opacity.DurationMs);
        Assert.Equal(AnimationCurve.Linear, spec.Opacity.Curve);
        Assert.Equal(0u, spec.StaggerMs);
    }

    [Theory]
    [InlineData("EnterContent", 40, 550u, 170u)]
    [InlineData("ShowEdgeUi", 70, 367u, 0u)]
    [InlineData("ShowPanel", 364, 550u, 0u)]
    public void The_entrances_carry_their_measured_offsets(
        string name, double offset, uint durationMs, uint fadeMs)
    {
        ref readonly var spec = ref AnimationCatalog.Of(Enum.Parse<Animation>(name));

        Assert.Equal(offset, spec.Offset.From);
        Assert.Equal(0, spec.Offset.To);
        Assert.Equal(durationMs, spec.Offset.DurationMs);
        Assert.Equal(AnimationCurve.Deceleration, spec.Offset.Curve);
        Assert.Equal(fadeMs, spec.Opacity.DurationMs);
    }

    [Fact]
    public void Exit_content_is_the_117_millisecond_linear_fade()
    {
        ref readonly var spec = ref Spec(Animation.ExitContent);

        Assert.Equal(117u, spec.Opacity.DurationMs);
        Assert.Equal(AnimationCurve.Linear, spec.Opacity.Curve);
    }

    [Fact]
    public void Hiding_reverses_the_entrance_it_belongs_to()
    {
        Assert.Equal(70, Spec(Animation.HideEdgeUi).Offset.To);
        Assert.Equal(0, Spec(Animation.HideEdgeUi).Offset.From);
        Assert.Equal(364, Spec(Animation.HidePanel).Offset.To);
        Assert.Equal(0, Spec(Animation.HidePanel).Offset.From);
    }

    [Fact]
    public void Fades_are_never_eased()
    {
        Assert.Equal(250u, Spec(Animation.FadeIn).Opacity.DurationMs);
        Assert.Equal(167u, Spec(Animation.FadeOut).Opacity.DurationMs);
        Assert.Equal(167u, Spec(Animation.CrossFadeIn).Opacity.DurationMs);
        Assert.Equal(167u, Spec(Animation.CrossFadeOut).Opacity.DurationMs);
        Assert.Equal(AnimationCurve.Linear, Spec(Animation.FadeIn).Opacity.Curve);
        Assert.Equal(AnimationCurve.Linear, Spec(Animation.FadeOut).Opacity.Curve);
        Assert.Equal(AnimationCurve.Linear, Spec(Animation.CrossFadeIn).Opacity.Curve);
        Assert.Equal(AnimationCurve.Linear, Spec(Animation.CrossFadeOut).Opacity.Curve);
    }

    [Fact]
    public void Pointer_feedback_is_the_tile_tilt_scale()
    {
        Assert.Equal(1, Spec(Animation.PointerDown).Scale.From);
        Assert.Equal(0.975, Spec(Animation.PointerDown).Scale.To);
        Assert.Equal(167u, Spec(Animation.PointerDown).Scale.DurationMs);
        Assert.Equal(0.975, Spec(Animation.PointerUp).Scale.From);
        Assert.Equal(1, Spec(Animation.PointerUp).Scale.To);
        Assert.Equal(167u, Spec(Animation.PointerUp).Scale.DurationMs);
    }

    [Fact]
    public void Reposition_staggers_at_33_capped_at_250()
    {
        ref readonly var spec = ref Spec(Animation.Reposition);

        Assert.Equal(367u, spec.Offset.DurationMs);
        Assert.Equal(AnimationCurve.Deceleration, spec.Offset.Curve);
        Assert.Equal(33u, spec.StaggerMs);
        Assert.Equal(250u, spec.StaggerCapMs);
    }

    [Fact]
    public void Adding_to_a_grid_repositions_over_400_and_grows_the_item_over_120()
    {
        ref readonly var spec = ref Spec(Animation.AddToGrid);

        Assert.Equal(400u, spec.Offset.DurationMs);
        Assert.Equal(0.85, spec.Scale.From);
        Assert.Equal(1, spec.Scale.To);
        Assert.Equal(120u, spec.Scale.DurationMs);
        Assert.Equal(120u, spec.Opacity.DurationMs);
    }

    [Fact]
    public void Deleting_from_a_grid_shrinks_on_the_departure_curve_after_a_60_millisecond_gap()
    {
        ref readonly var spec = ref Spec(Animation.DeleteFromGrid);

        Assert.Equal(AnimationCurve.Departure, spec.Scale.Curve);
        Assert.Equal(1, spec.Scale.From);
        Assert.Equal(0.85, spec.Scale.To);
        Assert.Equal(60u, spec.Offset.DelayMs);
        Assert.Equal(400u, spec.Offset.DurationMs);
    }

    [Fact]
    public void Expand_fades_after_a_200_millisecond_delay()
    {
        ref readonly var spec = ref Spec(Animation.Expand);

        Assert.Equal(200u, spec.Opacity.DelayMs);
        Assert.Equal(167u, spec.Opacity.DurationMs);
        Assert.Equal(367u, spec.Offset.DurationMs);
    }

    [Fact]
    public void A_popup_rises_50_pixels_and_fades_in_after_a_83_millisecond_delay()
    {
        ref readonly var spec = ref Spec(Animation.ShowPopup);

        Assert.Equal(MotionAxis.Y, spec.Axis);
        Assert.Equal(50, spec.Offset.From);
        Assert.Equal(367u, spec.Offset.DurationMs);
        Assert.Equal(83u, spec.Opacity.DelayMs);
        Assert.Equal(83u, spec.Opacity.DurationMs);
        Assert.Equal(83u, Spec(Animation.HidePopup).Opacity.DurationMs);
    }

    [Fact]
    public void Swipe_select_and_its_indicator_share_the_same_300_milliseconds()
    {
        Assert.Equal(300u, Spec(Animation.SwipeSelect).Opacity.DurationMs);
        Assert.Equal(300u, Spec(Animation.SwipeDeselect).Opacity.DurationMs);
        Assert.Equal(25, Spec(Animation.SwipeReveal).Offset.To);
        Assert.Equal(300u, Spec(Animation.SwipeReveal).Offset.DurationMs);
    }

    [Fact]
    public void A_drag_source_grows_and_dims_over_240_and_returns_over_500()
    {
        Assert.Equal(1.05, Spec(Animation.DragSourceStart).Scale.To);
        Assert.Equal(0.65, Spec(Animation.DragSourceStart).Opacity.To);
        Assert.Equal(240u, Spec(Animation.DragSourceStart).Scale.DurationMs);
        Assert.Equal(500u, Spec(Animation.DragSourceEnd).Scale.DurationMs);
        Assert.Equal(40, Spec(Animation.DragBetweenEnter).Offset.To);
        Assert.Equal(0.95, Spec(Animation.DragBetweenEnter).Scale.To);
        Assert.Equal(200u, Spec(Animation.DragBetweenEnter).Offset.DurationMs);
    }

    [Fact]
    public void Peek_takes_two_seconds_and_a_badge_rises_24_pixels_over_1333()
    {
        Assert.Equal(2000u, Spec(Animation.Peek).Offset.DurationMs);
        Assert.Equal(24, Spec(Animation.UpdateBadge).Offset.From);
        Assert.Equal(1333u, Spec(Animation.UpdateBadge).Offset.DurationMs);
        Assert.Equal(367u, Spec(Animation.UpdateBadge).Opacity.DurationMs);
    }

    [Fact]
    public void Every_entry_names_a_duration()
    {
        foreach (var spec in AnimationCatalog.All)
        {
            Assert.True(spec.DurationMs > 0, $"{spec.Name} has no duration");
        }
    }

    [Fact]
    public void The_stagger_cap_holds_a_two_hundred_item_grid_to_its_bound()
    {
        ref readonly var reposition = ref Spec(Animation.Reposition);

        Assert.Equal(0u, reposition.DelayFor(0));
        Assert.Equal(33u, reposition.DelayFor(1));
        Assert.Equal(231u, reposition.DelayFor(7));
        for (var index = 0; index < 200; index++)
        {
            Assert.True(reposition.DelayFor(index) <= reposition.StaggerCapMs);
        }

        Assert.Equal(250u, reposition.DelayFor(199));
        Assert.Equal(333u, Spec(Animation.EnterPage).DelayFor(199));
    }

    [Fact]
    public void An_unstaggered_entry_gives_every_item_the_same_delay()
    {
        ref readonly var spec = ref Spec(Animation.ShowPanel);

        Assert.Equal(0u, spec.DelayFor(0));
        Assert.Equal(0u, spec.DelayFor(40));
    }

    [Theory]
    [InlineData("Linear")]
    [InlineData("Deceleration")]
    [InlineData("Departure")]
    public void Every_curve_runs_from_zero_to_one_without_going_back(string name)
    {
        var curve = Enum.Parse<AnimationCurve>(name);

        Assert.Equal(0, Curves.Evaluate(curve, 0));
        Assert.Equal(1, Curves.Evaluate(curve, 1));

        var previous = 0.0;
        for (var step = 1; step <= 100; step++)
        {
            var value = Curves.Evaluate(curve, step / 100.0);
            Assert.True(value >= previous - 1e-9, $"{curve} went backwards at {step}");
            previous = value;
        }
    }

    [Fact]
    public void Deceleration_is_ahead_of_linear_for_most_of_its_run()
    {
        Assert.True(Curves.Evaluate(AnimationCurve.Deceleration, 0.25) > 0.25);
        Assert.True(Curves.Evaluate(AnimationCurve.Deceleration, 0.5) > 0.5);
    }

    [Fact]
    public void A_tween_reaches_the_end_of_its_track_and_stops()
    {
        var tween = default(Tween);
        tween.Start(AnimationCatalog.Of(Animation.EnterPage), nowMillis: 0);

        Assert.True(tween.IsRunning);
        Assert.Equal(100, tween.Offset, 6);
        Assert.Equal(0, tween.Alpha, 6);

        tween.Advance(500);
        Assert.True(tween.Offset is > 0 and < 100);
        Assert.Equal(1, tween.Alpha, 6);

        tween.Advance(1000);
        Assert.False(tween.IsRunning);
        Assert.Equal(0, tween.Offset, 6);
    }

    [Fact]
    public void A_staggered_tween_holds_its_start_through_the_delay()
    {
        var tween = default(Tween);
        tween.Start(AnimationCatalog.Of(Animation.Reposition), nowMillis: 0, index: 4);

        tween.Advance(100);
        Assert.Equal(1, tween.Offset, 6);

        tween.Advance(200);
        Assert.True(tween.Offset < 1);
    }

    [Fact]
    public void Settling_a_tween_lands_it_on_the_end_of_every_track()
    {
        var tween = default(Tween);
        tween.Start(AnimationCatalog.Of(Animation.DragSourceStart), nowMillis: 0);

        tween.Settle();

        Assert.False(tween.IsRunning);
        Assert.Equal(1.05, tween.Scale, 6);
        Assert.Equal(0.65f, tween.Alpha, 5);
    }
}
