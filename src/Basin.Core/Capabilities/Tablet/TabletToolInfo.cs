namespace Basin.Capabilities;

/// <summary>
/// Describes one tablet tool that an <see cref="ITabletSource"/> reports.
/// </summary>
/// <param name="Id">The id of the tool. It is unique within the source.</param>
/// <param name="Type">The physical type of the tool.</param>
/// <param name="HardwareSerial">The serial number that the hardware reports, or 0 when it reports none.</param>
/// <param name="Axes">The optional axes that the tool has.</param>
public readonly record struct TabletToolInfo(ulong Id, TabletToolType Type, ulong HardwareSerial, TabletToolAxis Axes);
