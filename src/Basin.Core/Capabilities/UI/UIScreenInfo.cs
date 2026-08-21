namespace Basin.Capabilities;

public readonly record struct UIScreenInfo(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    double Scale,
    bool IsPrimary);
