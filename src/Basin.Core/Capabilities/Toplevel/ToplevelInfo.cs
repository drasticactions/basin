namespace Basin.Capabilities;

public readonly record struct ToplevelInfo(
    ulong Id,
    string Title,
    string AppId,
    ToplevelState State,
    Surface? Surface,
    Box Geometry,
    Box ClientGeometry = default,
    string ResourceName = "",
    uint Pid = 0,
    ulong ParentId = 0,
    string AppMenuService = "",
    string AppMenuObjectPath = "");
