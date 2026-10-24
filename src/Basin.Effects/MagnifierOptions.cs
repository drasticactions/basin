namespace Basin.Effects;

/// <summary>
/// Holds the settings of a <see cref="MagnifierStage"/>.
/// </summary>
public readonly record struct MagnifierOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MagnifierOptions"/> struct with the default values.
    /// </summary>
    public MagnifierOptions()
    {
    }

    /// <summary>
    /// Gets the width of the lens, in output pixels. The default is 200.
    /// </summary>
    public int Width { get; init; } = 200;

    /// <summary>
    /// Gets the height of the lens, in output pixels. The default is 200.
    /// </summary>
    public int Height { get; init; } = 200;

    /// <summary>
    /// Gets the factor that one zoom-in or zoom-out step applies to the target. The default is 1.2.
    /// </summary>
    public double ZoomFactor { get; init; } = 1.2;

    /// <summary>
    /// Gets the zoom at construction. Values below 1 use 1.
    /// </summary>
    public double InitialZoom { get; init; } = 1.0;

    /// <summary>
    /// Gets the width of the black frame around the lens, in pixels. Zero draws no frame.
    /// </summary>
    public int FrameWidth { get; init; } = 5;

    /// <summary>
    /// Gets the time base of the zoom ramp, in milliseconds. Each frame changes the zoom by at least 20 percent toward the target.
    /// </summary>
    public double RampMillis { get; init; } = 500;
}
