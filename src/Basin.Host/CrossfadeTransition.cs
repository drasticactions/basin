namespace Basin.Host;

/// <summary>
/// Fades the inherited frame from full opacity to zero over the new compositor's scene. The frame is stretched over the whole output.
/// </summary>
public sealed class CrossfadeTransition : IHandoffTransition
{
    private readonly Func<double, double>? _easing;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossfadeTransition"/> class.
    /// </summary>
    /// <param name="duration">The length of the fade. Zero ends the fade at the first step.</param>
    /// <param name="easing">The curve that maps progress in [0, 1] to fade amount, or <see langword="null"/> for <see cref="OutCubic"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public CrossfadeTransition(TimeSpan duration, Func<double, double>? easing = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Duration = duration;
        _easing = easing;
    }

    /// <inheritdoc/>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Sets the frame to full opacity, stretched over the output with no clip.
    /// </summary>
    /// <param name="frame">The frame of one output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    public void Begin(HandoffFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Opacity = 1f;
        frame.Destination = new Box(0, 0, frame.OutputWidth, frame.OutputHeight);
        frame.Clip = default;
    }

    /// <summary>
    /// Sets the opacity to one minus the eased progress. The step does not allocate.
    /// </summary>
    /// <param name="frame">The frame of one output.</param>
    /// <param name="progress">The progress of the fade. Values outside [0, 1] are clamped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    public void Step(HandoffFrame frame, double progress)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var t = Math.Clamp(progress, 0, 1);
        var eased = _easing is { } easing ? easing(t) : OutCubic(t);
        frame.Opacity = (float)Math.Clamp(1 - eased, 0, 1);
    }

    /// <summary>
    /// Sets the opacity to zero.
    /// </summary>
    /// <param name="frame">The frame of one output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    public void End(HandoffFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Opacity = 0f;
    }

    /// <summary>
    /// Calculates a cubic ease-out: fast at the start and slow at the end.
    /// </summary>
    /// <param name="t">The progress, usually in [0, 1].</param>
    /// <returns>The eased value. It is 0 at 0 and 1 at 1.</returns>
    public static double OutCubic(double t)
    {
        var inverse = 1 - t;
        return 1 - (inverse * inverse * inverse);
    }
}
