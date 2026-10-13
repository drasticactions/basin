namespace Basin.Host;

public sealed class CrossfadeTransition : IHandoffTransition
{
    private readonly Func<double, double>? _easing;

    public CrossfadeTransition(TimeSpan duration, Func<double, double>? easing = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Duration = duration;
        _easing = easing;
    }

    public TimeSpan Duration { get; }

    public void Begin(HandoffFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Opacity = 1f;
        frame.Destination = new Box(0, 0, frame.OutputWidth, frame.OutputHeight);
        frame.Clip = default;
    }

    public void Step(HandoffFrame frame, double progress)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var t = Math.Clamp(progress, 0, 1);
        var eased = _easing is { } easing ? easing(t) : OutCubic(t);
        frame.Opacity = (float)Math.Clamp(1 - eased, 0, 1);
    }

    public void End(HandoffFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Opacity = 0f;
    }

    public static double OutCubic(double t)
    {
        var inverse = 1 - t;
        return 1 - (inverse * inverse * inverse);
    }
}
