namespace Basin.Ipc;

public readonly record struct IpcDiagNode(
    int Depth, string Kind, string? Name, string Path, int X, int Y, int Width, int Height, bool Enabled, string Details);
