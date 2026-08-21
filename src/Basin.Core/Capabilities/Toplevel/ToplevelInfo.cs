namespace Basin.Capabilities;

public readonly record struct ToplevelInfo(
    ulong Id,
    string Title,
    string AppId,
    ToplevelState State,
    Surface? Surface,
    Box Geometry);
