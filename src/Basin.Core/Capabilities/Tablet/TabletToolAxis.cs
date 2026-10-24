namespace Basin.Capabilities;

/// <summary>
/// Lists the optional axes of a tablet tool. A combination means that the tool has each named axis. Position is always present.
/// </summary>
[Flags]
public enum TabletToolAxis : uint
{
    /// <summary>
    /// No optional axis.
    /// </summary>
    None = 0,
    /// <summary>
    /// Tilt along X and Y.
    /// </summary>
    Tilt = 1,
    /// <summary>
    /// Tip pressure.
    /// </summary>
    Pressure = 2,
    /// <summary>
    /// Hover distance.
    /// </summary>
    Distance = 4,
    /// <summary>
    /// Rotation around the tool axis.
    /// </summary>
    Rotation = 8,
    /// <summary>
    /// A slider on the tool.
    /// </summary>
    Slider = 16,
    /// <summary>
    /// A wheel on the tool.
    /// </summary>
    Wheel = 32,
}
