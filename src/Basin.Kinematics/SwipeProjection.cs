namespace Basin.Kinematics;

/// <summary>
/// Decides where a released swipe lands from its position and its speed, with no threshold. Positions are in steps, where one step is the travel from one target to the next.
/// </summary>
public static class SwipeProjection
{
    /// <summary>
    /// The largest distance, in steps, that the rubber band lets a position go past an end.
    /// </summary>
    public const double OverscrollLimit = 0.05;

    /// <summary>
    /// The decay rate of the release speed, per second, that applies when a friction is not positive. A touchpad coast loses 0.3 percent of its speed each millisecond.
    /// </summary>
    public const double DefaultFriction = 3.0045;

    private const double OverscrollStiffness = 0.5;

    /// <summary>
    /// Maps a raw position to the position on screen. Inside the range the position does not change. Past an end, the extra travel approaches <see cref="OverscrollLimit"/> and never reaches it.
    /// </summary>
    /// <param name="position">The raw position, in steps.</param>
    /// <param name="minimum">The first step.</param>
    /// <param name="maximum">The last step. It must not be less than <paramref name="minimum"/>.</param>
    /// <returns>The position on screen, in steps.</returns>
    public static double RubberBand(double position, double minimum, double maximum)
    {
        var clamped = Math.Clamp(position, minimum, maximum);
        var excess = position - clamped;
        return clamped + Math.CopySign(Overscroll(Math.Abs(excess)), excess);
    }

    /// <summary>
    /// Calculates the slope of <see cref="RubberBand"/> at a raw position. It is the fraction of the finger speed that the screen shows.
    /// </summary>
    /// <param name="position">The raw position, in steps.</param>
    /// <param name="minimum">The first step.</param>
    /// <param name="maximum">The last step. It must not be less than <paramref name="minimum"/>.</param>
    /// <returns>1 inside the range, and a value that goes down toward zero past an end.</returns>
    public static double RubberBandSlope(double position, double minimum, double maximum)
    {
        if (position >= minimum && position <= maximum)
        {
            return 1;
        }

        var excess = Math.Abs(position - Math.Clamp(position, minimum, maximum));
        var denominator = 1 + (OverscrollStiffness * excess / OverscrollLimit);
        return OverscrollStiffness / (denominator * denominator);
    }

    /// <summary>
    /// Decides the step on which a released swipe lands. It projects where the travel comes to rest, applies the rubber band, and rounds to the nearest step that exists.
    /// </summary>
    /// <param name="position">The raw position at the release, in steps, with no clamp. It includes the step from which the swipe started.</param>
    /// <param name="velocity">The release speed, in steps per second. Use the speed after a sample with no motion at the release time.</param>
    /// <param name="first">The first step.</param>
    /// <param name="last">The last step. A value less than <paramref name="first"/> counts as <paramref name="first"/>.</param>
    /// <param name="friction">The decay rate of the speed, per second. A value that is not positive uses <see cref="DefaultFriction"/>.</param>
    /// <returns>The target step, the position on screen, and the speed for the settle spring.</returns>
    public static SwipeRelease Release(double position, double velocity, int first, int last, double friction = DefaultFriction)
    {
        last = Math.Max(first, last);
        if (!double.IsFinite(position))
        {
            position = first;
        }

        if (!double.IsFinite(velocity))
        {
            velocity = 0;
        }

        var decay = friction > 0 ? friction : DefaultFriction;
        var coast = Math.CopySign(Fling.RestDistance(Math.Abs(velocity), decay, 0), velocity);
        var projected = RubberBand(position + coast, first, last);
        var target = (int)Math.Round(Math.Clamp(projected, first, last), MidpointRounding.AwayFromZero);
        var slope = RubberBandSlope(position, first, last);
        return new SwipeRelease(target, RubberBand(position, first, last), velocity * slope);
    }

    private static double Overscroll(double excess) =>
        OverscrollLimit * (1 - (1 / (1 + (OverscrollStiffness * excess / OverscrollLimit))));
}
