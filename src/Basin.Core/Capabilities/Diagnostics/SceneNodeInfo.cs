namespace Basin.Capabilities;

public readonly record struct SceneNodeInfo(
    int Depth,
    string Kind,
    string? Name,
    string Path,
    int X,
    int Y,
    int Width,
    int Height,
    bool Enabled,
    string Details);
