namespace Basin.Ipc;

public readonly record struct IpcWindow(
    ulong Id,
    string Title,
    string AppId,
    IpcWindowState State,
    IpcBox Geometry,
    IpcBox ClientGeometry,
    uint Pid,
    ulong? Parent,
    string? Output,
    ulong? Workspace);
