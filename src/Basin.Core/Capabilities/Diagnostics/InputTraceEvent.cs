namespace Basin.Capabilities;

public readonly record struct InputTraceEvent(
    string Kind,
    Surface? Surface,
    double X,
    double Y,
    double LayoutX,
    double LayoutY,
    uint Code,
    bool Pressed,
    double Value,
    string? Detail);
