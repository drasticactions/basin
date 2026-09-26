namespace Basin.Ipc;

public readonly record struct IpcOutput(
    string Name,
    string Description,
    string Make,
    string Model,
    string Serial,
    bool Enabled,
    IpcOutputMode Mode,
    double Refresh,
    double Scale,
    string Transform,
    bool AdaptiveSync,
    IpcSize PhysicalSize,
    IpcOptionalBox Geometry,
    bool? Power,
    bool Pointer);
