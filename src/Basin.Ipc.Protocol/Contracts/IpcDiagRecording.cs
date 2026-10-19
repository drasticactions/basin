namespace Basin.Ipc;

public readonly record struct IpcDiagRecording(
    string Path,
    int Frames,
    int Width,
    int Height,
    int ImageWidth,
    int ImageHeight,
    double LumaLow,
    double LumaHigh,
    double PeakLow,
    double PeakHigh,
    int DeltaMax,
    bool TimedOut,
    IpcDiagRecordFrame[] PerFrame,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] byte[]? Png = null);
