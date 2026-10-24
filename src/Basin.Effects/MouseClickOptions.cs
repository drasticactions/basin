namespace Basin.Effects;

/// <summary>
/// Holds the settings of a <see cref="MouseClickEffect"/>. The defaults are KWin's.
/// </summary>
public readonly record struct MouseClickOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseClickOptions"/> struct with KWin's defaults.
    /// </summary>
    public MouseClickOptions()
    {
    }

    /// <summary>
    /// Gets the life of one ring, in milliseconds. The default is 300.
    /// </summary>
    public double RingLifeMillis { get; init; } = 300;

    /// <summary>
    /// Gets the radius that a ring grows to, in pixels. The default is 20.
    /// </summary>
    public double RingSize { get; init; } = 20;

    /// <summary>
    /// Gets the number of rings per click, started one after another. The default is 2.
    /// </summary>
    public int RingCount { get; init; } = 2;

    /// <summary>
    /// Gets the width of a ring line, in pixels. The default is 1.
    /// </summary>
    public double LineWidth { get; init; } = 1.0;

    /// <summary>
    /// Gets the ring color for the left button. The default is red.
    /// </summary>
    public RenderColor LeftColor { get; init; } = new(1f, 0f, 0f, 1f);

    /// <summary>
    /// Gets the ring color for the middle button. The default is green.
    /// </summary>
    public RenderColor MiddleColor { get; init; } = new(0f, 1f, 0f, 1f);

    /// <summary>
    /// Gets the ring color for the right button and every other button. The default is blue.
    /// </summary>
    public RenderColor RightColor { get; init; } = new(0f, 0f, 1f, 1f);
}
