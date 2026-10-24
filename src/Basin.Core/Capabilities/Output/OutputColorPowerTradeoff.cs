namespace Basin.Capabilities;

/// <summary>
/// Whether an output prefers power saving or color accuracy.
/// </summary>
public enum OutputColorPowerTradeoff
{
    /// <summary>
    /// Power saving features, for example adaptive backlight management, can change colors.
    /// </summary>
    Efficiency = 0,
    /// <summary>
    /// Colors stay accurate, so adaptive backlight management is turned off.
    /// </summary>
    Accuracy = 1,
}
