namespace Basin.Capabilities;

/// <summary>
/// A device orientation sensor, for example an accelerometer, that tells how to rotate a built-in display.
/// </summary>
public interface IOrientationSource
{
    /// <summary>
    /// Gets a value indicating whether a sensor is present.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the transform that the current orientation needs, or <see langword="null"/> when the orientation is not known.
    /// </summary>
    OutputTransform? Orientation { get; }

    /// <summary>
    /// Starts or stops reading the sensor. A sensor that is not enabled can stop reporting.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to start reading.</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Occurs when <see cref="IsAvailable"/> or <see cref="Orientation"/> changes.
    /// </summary>
    event Action? Changed;
}
