namespace Basin.Capabilities;

public readonly record struct TabletToolInfo(ulong Id, TabletToolType Type, ulong HardwareSerial, TabletToolAxis Axes);
