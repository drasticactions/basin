namespace Basin.Capabilities;

/// <summary>
/// When an output follows the orientation sensor.
/// </summary>
public enum OutputAutoRotatePolicy
{
    /// <summary>
    /// The output keeps the transform that the configuration sets.
    /// </summary>
    Never = 0,
    /// <summary>
    /// The output follows the sensor only while the device is in tablet mode.
    /// </summary>
    InTabletMode = 1,
    /// <summary>
    /// The output always follows the sensor.
    /// </summary>
    Always = 2,
}
