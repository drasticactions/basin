namespace Basin.Capabilities;

public readonly record struct TabletPadEvent(
    TabletPadEventKind Kind,
    uint Group,
    uint Index,
    double Value,
    bool Pressed);
