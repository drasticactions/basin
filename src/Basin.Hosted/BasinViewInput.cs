namespace Basin.Hosted;

public readonly record struct BasinViewInput(
    BasinViewInputKind Kind,
    uint TimeMs,
    double X,
    double Y,
    uint Code,
    bool Pressed,
    double DeltaX,
    double DeltaY,
    int TouchId);
