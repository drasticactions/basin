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
        Assert.Equal(AnimationCurve.Deceleration, spec.Opacity.Curve);
        Assert.Equal(83u, spec.StaggerMs);
        Assert.Equal(333u, spec.StaggerCapMs);
    }

    [Theory]
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
        Assert.Equal(167u, Spec(Animation.CrossFadeOut).Opacity.DurationMs);
        Assert.Equal(AnimationCurve.Linear, Spec(Animation.FadeIn).Opacity.Curve);
        Assert.Equal(AnimationCurve.Linear, Spec(Animation.CrossFadeOut).Opacity.Curve);
    }

    [Fact]
    public void A_drag_source_grows_and_dims_over_240_and_returns_over_500()
    {
        Assert.Equal(1.05, Spec(Animation.DragSourceStart).Scale.To);
        Assert.Equal(0.65, Spec(Animation.DragSourceStart).Opacity.To);
        Assert.Equal(240u, Spec(Animation.DragSourceStart).Scale.DurationMs);
        Assert.Equal(500u, Spec(Animation.DragSourceEnd).Scale.DurationMs);
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
        ref readonly var enter = ref Spec(Animation.EnterPage);

        Assert.Equal(0u, enter.DelayFor(0));
        Assert.Equal(83u, enter.DelayFor(1));
        Assert.Equal(249u, enter.DelayFor(3));
        for (var index = 0; index < 200; index++)
        {
            Assert.True(enter.DelayFor(index) <= enter.StaggerCapMs);
        }

        Assert.Equal(333u, enter.DelayFor(199));
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
        tween.Start(AnimationCatalog.Of(Animation.EnterPage), nowMillis: 0, index: 4);

        tween.Advance(100);
        Assert.Equal(100, tween.Offset, 6);

        tween.Advance(500);
        Assert.True(tween.Offset < 100);
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
