namespace Basin.Effects;

/// <summary>
/// Holds the settings of a <see cref="ShakeCursorEffect"/>.
/// </summary>
public readonly record struct ShakeCursorOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShakeCursorOptions"/> struct with the default values.
    /// </summary>
    public ShakeCursorOptions()
    {
    }

    /// <summary>
    /// Gets the rolling interval, in milliseconds, in which the shake detector looks for direction changes. The default is 1000.
    /// </summary>
    public double TimeIntervalMillis { get; init; } = 1000;

    /// <summary>
    /// Gets the ratio of path length to bounding diagonal that counts as a shake. The default is 4.
    /// </summary>
    public double Sensitivity { get; init; } = 4;

    /// <summary>
    /// Gets the cursor scale after the first shake. The default is 3.
    /// </summary>
    public double Magnification { get; init; } = 3;

    /// <summary>
    /// Gets the scale that each further shake adds while the cursor is enlarged. The default is 1.
    /// </summary>
    public double OverMagnification { get; init; } = 1;

    /// <summary>
    /// Gets the time, in milliseconds, from the last shake until the cursor shrinks back. The default is 2000.
    /// </summary>
    public double DeflateAfterMillis { get; init; } = 2000;

    /// <summary>
    /// Gets the length of a scale change, in milliseconds. The animation factor does not apply to it. The default is 200.
    /// </summary>
    public double RampMillis { get; init; } = 200;
}
