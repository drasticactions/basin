namespace Basin.Effects;

/// <summary>
/// Holds the settings of a <see cref="ZoomStage"/>.
/// </summary>
public readonly record struct ZoomOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomOptions"/> struct with the default values.
    /// </summary>
    public ZoomOptions()
    {
    }

    /// <summary>
    /// Gets the factor that one zoom-in or zoom-out step applies to the target. It also sets the length of the zoom animation. The default is 1.2.
    /// </summary>
    public double ZoomFactor { get; init; } = 1.2;

    /// <summary>
    /// Gets the zoom at construction. Values below 1 use 1.
    /// </summary>
    public double InitialZoom { get; init; } = 1.0;

    /// <summary>
    /// Gets how the view follows the cursor. The default is <see cref="ZoomTracking.Proportional"/>.
    /// </summary>
    public ZoomTracking MouseTracking { get; init; } = ZoomTracking.Proportional;

    /// <summary>
    /// Gets a value indicating whether the view follows the rectangle that <see cref="ZoomStage.Target"/> reports. The target chooses the focus, the text caret, or either.
    /// </summary>
    public bool FocusTracking { get; init; } = false;

    /// <summary>
    /// Gets the time, in milliseconds, that the cursor must be still before a focus report moves the view. The default is 350.
    /// </summary>
    public double FocusDelayMillis { get; init; } = 350;

    /// <summary>
    /// Gets the divisor of the screen size for one pan step of the move methods. The default is 20.
    /// </summary>
    public double MoveFactor { get; init; } = 20.0;

    /// <summary>
    /// Gets the zoom at and above which the shader draws a pixel grid. The default is 15.
    /// </summary>
    public double PixelGridZoom { get; init; } = 15.0;
}
